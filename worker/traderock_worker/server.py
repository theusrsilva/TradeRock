"""Laço principal do worker: pedidos JSON (uma linha cada) na entrada, respostas e eventos na saída.

Pedido:   {"id": 1, "op": "hello"}
Resposta: {"id": 1, "ok": true, "result": {...}}  ou  {"id": 1, "ok": false, "error": {"code", "message"}}
Evento:   {"event": "trade" | "trade_end" | "debug", ...}

Uma operação de rádio por vez (card_test, beacon_test, start_trade ou start_pokeldn). `cancel` interrompe a operação
em andamento.
"""

import base64
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import threading

from . import PROTOCOL, VERSION
from .protocol import MAX_LINE, RequestError, beacon_params, phy as check_phy, pokeldn_params, trade_params
from .radio import NetworkConfig, Radio, SAFE_PATH, which

GRACEFUL_CANCEL_SECONDS = 30   # primeiro SIGINT: o motor sai da sala e espera o Switch fechar
HARD_CANCEL_SECONDS = 10       # segundo SIGINT: encerra na hora; depois disso, SIGKILL
CARD_TEST_TIMEOUT = 60
BEACON_TEST_MARGIN = 30        # além do tempo de escuta pedido: criar o monitor, devolver o rádio
# O motor chama iw e ip (embutidos no worker) e sysctl (procps, presente em praticamente todo Linux);
# sem eles ele quebra no meio (FileNotFoundError) ou deixa de receber os anúncios do Switch.
REQUIRED_TOOLS = ("iw", "ip", "sysctl")


class Server:
    def __init__(self, output, *, radio=None, netconf=None, child_cmd=None, info=None,
                 require_root=True, which_tool=which):
        self._out = output
        self._out_lock = threading.Lock()
        self.radio = radio or Radio()
        self.netconf = netconf or NetworkConfig()
        self.child_cmd = child_cmd or default_child_cmd
        self.info = info or {}
        self.require_root = require_root
        self.which = which_tool
        self._op = None
        self._op_lock = threading.Lock()
        self._op_thread = None
        self._child = None
        self._cancel = False

    # -- saída -------------------------------------------------------------------------------
    def send(self, obj):
        line = json.dumps(obj, ensure_ascii=False)
        with self._out_lock:
            self._out.write(line + "\n")
            self._out.flush()

    def reply(self, req_id, result=None):
        self.send({"id": req_id, "ok": True, "result": result or {}})

    def fail(self, req_id, code, message):
        self.send({"id": req_id, "ok": False, "error": {"code": code, "message": message}})

    def debug(self, text):
        self.send({"event": "debug", "text": text})

    # -- entrada -----------------------------------------------------------------------------
    def serve(self, stream):
        try:
            while True:
                raw = stream.readline(MAX_LINE + 1)
                if not raw:
                    break
                if len(raw) > MAX_LINE:
                    self.fail(None, "invalid_request", "mensagem grande demais")
                    break
                self.handle(raw)
        finally:
            self.shutdown()

    def handle(self, raw):
        try:
            msg = json.loads(raw)
        except ValueError:
            self.fail(None, "invalid_request", "JSON inválido")
            return
        if not isinstance(msg, dict):
            self.fail(None, "invalid_request", "objeto esperado")
            return
        req_id = msg.get("id")
        op = msg.get("op")
        handler = {
            "hello": self.op_hello,
            "status": self.op_status,
            "card_test": self.op_card_test,
            "beacon_test": self.op_beacon_test,
            "start_trade": self.op_start_trade,
            "start_pokeldn": self.op_start_pokeldn,
            "cancel": self.op_cancel,
            "net_status": self.op_net_status,
            "net_install": self.op_net_install,
        }.get(op)
        if handler is None:
            self.fail(req_id, "unknown_op", f"operação desconhecida: {op!r}")
            return
        if op not in ("hello", "status", "cancel", "net_status") and self.require_root and os.geteuid() != 0:
            self.fail(req_id, "not_root", "o worker precisa rodar como root (via pkexec)")
            return
        try:
            handler(req_id, msg)
        except RequestError as e:
            self.fail(req_id, e.code, str(e))

    # -- operações rápidas -------------------------------------------------------------------
    def op_hello(self, req_id, _msg):
        self.reply(req_id, {
            "version": VERSION,
            "protocol": PROTOCOL,
            "root": os.geteuid() == 0,
            "tools": {t: self.which(t) for t in ("iw", "ip", "nmcli", "sysctl")},
            "missingTools": [t for t in REQUIRED_TOOLS if self.which(t) is None],
            **self.info,
        })

    def op_status(self, req_id, _msg):
        self.reply(req_id, {"busy": self._op})

    def op_net_status(self, req_id, _msg):
        self.reply(req_id, self.netconf.status())

    def op_net_install(self, req_id, _msg):
        if not self._begin("net_install", req_id):
            return
        try:
            self.reply(req_id, self.netconf.install())
        except OSError as e:
            self.fail(req_id, "net_install_failed", str(e))
        finally:
            self._end()

    def op_cancel(self, req_id, _msg):
        self._cancel = True
        child = self._child
        if child is not None and child.poll() is None:
            child.send_signal(signal.SIGINT)
            threading.Thread(target=self._escalate, args=(child,), daemon=True).start()
        self.reply(req_id, {"cancelling": self._op is not None})

    def _escalate(self, child):
        try:
            child.wait(GRACEFUL_CANCEL_SECONDS)
            return
        except subprocess.TimeoutExpired:
            pass
        child.send_signal(signal.SIGINT)
        try:
            child.wait(HARD_CANCEL_SECONDS)
        except subprocess.TimeoutExpired:
            child.kill()

    # -- uma operação de rádio por vez ------------------------------------------------------
    def _begin(self, name, req_id):
        with self._op_lock:
            if self._op is not None:
                self.fail(req_id, "busy", f"o worker está ocupado ({self._op})")
                return False
            self._op = name
            self._cancel = False
            return True

    def _end(self):
        with self._op_lock:
            self._op = None
            self._child = None

    def _spawn(self, kind, env=None, cwd=None):
        child = subprocess.Popen(
            self.child_cmd(kind), stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, cwd=cwd, env=env or child_env(), start_new_session=True)
        self._child = child
        threading.Thread(target=self._pump_stderr, args=(child,), daemon=True).start()
        return child

    def _pump_stderr(self, child):
        for raw in child.stderr:
            text = raw.decode("utf-8", "replace").rstrip()
            if text:
                self.debug(text[:2000])

    # -- teste da placa ----------------------------------------------------------------------
    def _require_tools(self):
        missing = [t for t in REQUIRED_TOOLS if self.which(t) is None]
        if missing:
            raise RequestError("missing_tools", ",".join(missing))

    def op_card_test(self, req_id, msg):
        phy = check_phy(msg.get("phy"))
        self._require_tools()
        if not self.radio.phy_exists(phy):
            raise RequestError("phy_not_found", f"{phy} não existe")
        if not self._begin("card_test", req_id):
            return
        self._op_thread = threading.Thread(target=self._card_test, args=(req_id, phy), daemon=True)
        self._op_thread.start()

    def _card_test(self, req_id, phy):
        self._run_test(req_id, phy, "card", {"phy": phy}, CARD_TEST_TIMEOUT, "card_test_crashed")

    def _run_test(self, req_id, phy, kind, params, timeout, crash_code, tmp=None):
        """Roda um filho de teste (uma linha JSON de entrada, um "result" na saída) com o rádio
        emprestado; devolve o rádio antes de responder."""
        snapshot = self.radio.snapshot(phy)
        result, error = None, None
        try:
            self.radio.take(snapshot)
            if self._cancel:
                raise Cancelled()
            child = self._spawn(kind)
            child.stdin.write((json.dumps(params) + "\n").encode())
            child.stdin.close()
            timer = threading.Timer(timeout, child.kill)
            timer.start()
            try:
                for raw in child.stdout:
                    try:
                        msg = json.loads(raw)
                    except ValueError:
                        continue
                    if msg.get("type") == "result":
                        result = msg["result"]
                    elif msg.get("type") == "progress":
                        self.debug(f"{kind}: {json.dumps(msg, ensure_ascii=False)}"[:2000])
                child.wait()
            finally:
                timer.cancel()
            if result is None:
                error = f"o teste terminou sem resultado (código {child.returncode})"
        except Cancelled:
            result = {"passed": False, "code": "cancelled", "cancelled": True}
        except Exception as e:  # noqa: BLE001
            error = f"{type(e).__name__}: {e}"
        finally:
            # Primeiro devolve o rádio, depois responde: quando o app recebe a resposta, o Wi-Fi já voltou.
            self.radio.restore(snapshot)
            if tmp is not None:
                shutil.rmtree(tmp, ignore_errors=True)
            self._end()
        if result is None:
            self.fail(req_id, crash_code, error)
        else:
            self.reply(req_id, result)

    # -- teste com o Switch (beacon) ---------------------------------------------------------
    def op_beacon_test(self, req_id, msg):
        params = beacon_params(msg)
        self._require_tools()
        if not self.radio.phy_exists(params.phy):
            raise RequestError("phy_not_found", f"{params.phy} não existe")
        if not self._begin("beacon_test", req_id):
            return
        self._op_thread = threading.Thread(target=self._beacon_test, args=(req_id, params), daemon=True)
        self._op_thread.start()

    def _beacon_test(self, req_id, p):
        tmp = tempfile.mkdtemp(prefix="traderock-beacon-")  # 0700, do root
        try:
            keys = os.path.join(tmp, "prod.keys")
            fd = os.open(keys, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, "wb") as f:
                f.write(p.keys)
        except OSError as e:
            shutil.rmtree(tmp, ignore_errors=True)
            self._end()
            self.fail(req_id, "beacon_test_crashed", str(e))
            return
        self._run_test(req_id, p.phy, "beacon", {"phy": p.phy, "keys": keys, "seconds": p.seconds},
                       p.seconds + BEACON_TEST_MARGIN, "beacon_test_crashed", tmp=tmp)

    # -- troca ---------------------------------------------------------------------------------
    def op_start_trade(self, req_id, msg):
        params = trade_params(msg)
        self._require_tools()
        if not self.radio.phy_exists(params.phy):
            raise RequestError("phy_not_found", f"{params.phy} não existe")
        if not self._begin("trade", req_id):
            return
        self.reply(req_id, {"started": True})
        self._op_thread = threading.Thread(target=self._trade, args=(params,), daemon=True)
        self._op_thread.start()

    def _trade(self, p):
        end = {"outcome": "failed", "code": "engine_error", "message": None, "retryable": False}
        sent = set()
        tmp = tempfile.mkdtemp(prefix="traderock-trade-")  # 0700, do root
        snapshot = self.radio.snapshot(p.phy)
        try:
            keys = os.path.join(tmp, "prod.keys")
            fd = os.open(keys, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, "wb") as f:
                f.write(p.keys)
            party = []
            for i, data in enumerate(p.party):
                path = os.path.join(tmp, f"party{i}.pk3")
                with open(path, "wb") as f:
                    f.write(data)
                party.append(path)
            out_dir = os.path.join(tmp, "out")
            os.mkdir(out_dir, 0o700)

            if self._cancel:
                raise Cancelled()
            child = self._spawn("trade", env=child_env(p.approval_seconds), cwd=tmp)
            child.stdin.write((json.dumps({
                "phy": p.phy, "keys": keys, "party": party, "slots": list(p.slots),
                "game": p.game, "name": p.name, "tid": p.tid, "sid": p.sid,
                "out": os.path.join(out_dir, "received.pk3"),
            }) + "\n").encode())
            child.stdin.close()

            for raw in child.stdout:
                try:
                    msg = json.loads(raw)
                except ValueError:
                    continue
                kind = msg.get("type")
                if kind == "event":
                    self.send({"event": "trade", "kind": msg["kind"], "message": msg.get("message"),
                               "tradeIndex": msg.get("tradeIndex")})
                elif kind == "received":
                    sent.add(msg["tradeIndex"])
                    self.send({"event": "trade", "kind": "trade_done", "tradeIndex": msg["tradeIndex"],
                               "data": msg["data"]})
                elif kind == "debug":
                    self.debug(msg.get("text", ""))
                elif kind == "end":
                    end = {k: msg.get(k) for k in ("outcome", "code", "message", "retryable")}
            child.wait()

            # Filho encerrado à força depois de gravar: o recebido ainda está no disco.
            for index, path in received_files(out_dir):
                if index not in sent:
                    with open(path, "rb") as f:
                        data = base64.b64encode(f.read()).decode("ascii")
                    sent.add(index)
                    self.send({"event": "trade", "kind": "trade_done", "tradeIndex": index, "data": data})
        except Cancelled:
            pass
        except Exception as e:  # noqa: BLE001
            end = {"outcome": "failed", "code": "engine_error", "message": f"{type(e).__name__}: {e}",
                   "retryable": False}
        finally:
            self.radio.restore(snapshot)
            shutil.rmtree(tmp, ignore_errors=True)
            if self._cancel and end.get("outcome") != "completed":
                end = {"outcome": "cancelled", "code": "cancelled", "message": None, "retryable": False}
            end["received"] = len(sent)
            self._end()
            self.send({"event": "trade_end", **end})

    # -- troca pelo pokeldn (jogos do Switch, D23) ------------------------------------------------
    def op_start_pokeldn(self, req_id, msg):
        params = pokeldn_params(msg)
        self._require_tools()
        if not self.radio.phy_exists(params.phy):
            raise RequestError("phy_not_found", f"{params.phy} não existe")
        if not self._begin("pokeldn", req_id):
            return
        self.reply(req_id, {"started": True})
        self._op_thread = threading.Thread(target=self._pokeldn, args=(params,), daemon=True)
        self._op_thread.start()

    def _pokeldn(self, p):
        """O filho (pokeldn_trade.py) fala o próprio protocolo; cada mensagem vai ao app como {"event": "pokeldn"}."""
        tmp = tempfile.mkdtemp(prefix="traderock-pokeldn-")  # 0700, do root
        snapshot = self.radio.snapshot(p.phy)
        code = None
        try:
            keys = os.path.join(tmp, "prod.keys")
            fd = os.open(keys, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, "wb") as f:
                f.write(p.keys)
            if self._cancel:
                raise Cancelled()
            child = self._spawn("pokeldn", cwd=tmp)
            request = dict(p.request, keys=keys, radio=None, phy=p.phy)
            child.stdin.write((json.dumps(request) + "\n").encode())
            child.stdin.close()
            for raw in child.stdout:
                try:
                    msg = json.loads(raw)
                except ValueError:
                    continue
                if isinstance(msg, dict):
                    self.send({"event": "pokeldn", "message": msg})
            child.stdout.close()
            code = child.wait()
        except Cancelled:
            pass
        except Exception as e:  # noqa: BLE001
            self.send({"event": "pokeldn", "message": {"type": "error", "code": "engine_error",
                                                        "message": f"{type(e).__name__}: {e}"}})
        finally:
            self.radio.restore(snapshot)
            shutil.rmtree(tmp, ignore_errors=True)
            cancelled = self._cancel
            self._end()
            self.send({"event": "pokeldn_end", "exitCode": code, "cancelled": cancelled})

    # -- encerramento ----------------------------------------------------------------------------
    def shutdown(self):
        """Entrada fechada (o app saiu) ou sinal: cancela, espera o rádio voltar e sai."""
        if self._op is not None:
            try:
                self.op_cancel(None, {})
            except Exception:  # noqa: BLE001 - a saída pode já estar fechada
                pass
        thread = self._op_thread
        if thread is not None:
            thread.join(GRACEFUL_CANCEL_SECONDS + HARD_CANCEL_SECONDS + 15)


class Cancelled(Exception):
    pass


RECEIVED_RE = re.compile(r"^received(?:_trade(\d+)_.*)?\.pk3$")


def received_files(out_dir):
    """Arquivos gravados pelo motor: received.pk3 (1 troca) ou received_trade<k>_<espécie>.pk3."""
    found = []
    try:
        names = sorted(os.listdir(out_dir))
    except OSError:
        return found
    for name in names:
        m = RECEIVED_RE.match(name)
        if m:
            index = int(m.group(1)) - 1 if m.group(1) else 0
            found.append((index, os.path.join(out_dir, name)))
    return found


def child_env(approval_seconds=60):
    """Ambiente dos filhos: mínimo, e com os ajustes dos patches do motor.

    FRLG_JOIN_ATTEMPTS=1: cada tentativa do TradeRock é uma tentativa do motor (o retry fica no
    coordenador do app, que conta e mostra as tentativas). FRLG_JOIN_IP_WAIT: quanto esperar a pessoa
    aprovar a entrada no Switch.
    """
    return {
        "PATH": SAFE_PATH,
        "LANG": "C.UTF-8",
        "PYTHONIOENCODING": "utf-8",
        "FRLG_JOIN_ATTEMPTS": "1",
        "FRLG_JOIN_IP_WAIT": str(approval_seconds),
    }


def default_child_cmd(kind):
    run_py = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "run.py")
    return [sys.executable, "-I", "-B", run_py, "--child", kind]


def main(info):
    os.umask(0o077)
    out = os.fdopen(os.dup(1), "w", buffering=1, encoding="utf-8")
    os.dup2(2, 1)  # nada além do protocolo sai na saída padrão
    sys.stdout = sys.stderr
    server = Server(out, info=info)

    def on_signal(_signum, _frame):
        raise SystemExit(0)

    signal.signal(signal.SIGTERM, on_signal)
    signal.signal(signal.SIGHUP, on_signal)
    signal.signal(signal.SIGINT, signal.SIG_IGN)  # Ctrl+C no terminal do app não mata o worker
    try:
        server.serve(sys.stdin.buffer)
    except SystemExit:
        server.shutdown()
    return 0
