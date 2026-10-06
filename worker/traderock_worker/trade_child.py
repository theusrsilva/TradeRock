"""Processo filho (root) que roda UMA tentativa de troca com o frlg-ldn-trade.

Fica num processo separado porque o motor instala o próprio tratador de SIGINT (o primeiro Ctrl+C
sai da sala com educação, o segundo encerra na hora) e isso só funciona na thread principal.

Não é um fork do motor: importa o frlgtrade do submódulo (com os patches de engines/patches) e
troca o logger dele por um que emite eventos. A entrada é uma linha JSON na entrada padrão; a saída
são linhas JSON num descritor próprio (o `print` do motor vai para a saída de erro e vira log).
"""

import base64
import json
import logging
import os
import signal
import sys
import traceback

from .beacon_child import CHANNELS
from .events import classify, join_failure
from .radio import supported_channels


class Emitter:
    def __init__(self, fd):
        self._out = os.fdopen(fd, "w", buffering=1, encoding="utf-8")

    def __call__(self, obj):
        self._out.write(json.dumps(obj, ensure_ascii=False) + "\n")
        self._out.flush()


class BeaconWatch(logging.Handler):
    """A biblioteca LDN avisa quando vê um anúncio LDN que não decifra com as chaves."""

    def __init__(self):
        super().__init__(logging.WARNING)
        self.undecryptable = 0

    def emit(self, record):
        if "Failed to parse advertisement frame" in record.getMessage():
            self.undecryptable += 1


def main(engine_dir):
    emit = Emitter(os.dup(1))
    os.dup2(2, 1)  # print() do motor vai para a saída de erro (log de diagnóstico)
    sys.stdout = sys.stderr
    signal.signal(signal.SIGINT, signal.default_int_handler)  # o pai ignora SIGINT; o filho não
    params = json.loads(sys.stdin.readline())

    sys.path.insert(0, engine_dir)
    import frlgtrade
    from frlgsim import config as configmod
    from frlgsim import crypto as cryptomod
    from frlgsim import trade_runtime as runtime

    state = {"offered": 0, "joined": False, "rejected": False, "cancelled_by_host": False,
             "sent": set(), "announced_beacon": False}

    def on_info(message):
        kind = classify(message)
        index = None
        if kind == "trade_offered":
            index = state["offered"]
            state["offered"] += 1
        elif kind in ("confirming", "trade_cancelled"):
            index = max(state["offered"] - 1, 0)
        if kind == "join_requested" and not state["announced_beacon"]:
            # ldn.scan só devolve salas cujo anúncio decifrou com as chaves.
            state["announced_beacon"] = True
            emit({"type": "event", "kind": "beacon_decrypted", "message": "Sala encontrada e decifrada."})
        if kind == "joined":
            state["joined"] = True
        if kind == "trade_cancelled":
            state["cancelled_by_host"] = True
        if message.startswith("Host rejected our join"):
            state["rejected"] = True
        emit({"type": "event", "kind": kind, "message": message, "tradeIndex": index})

    class EventLog(runtime.ConsoleLog):
        def __call__(self, *parts):
            text = " ".join(str(p) for p in parts).strip()
            if text:
                emit({"type": "debug", "text": f"{self.prefix} {text}".strip()[:2000]})

        def info(self, *parts):
            text = " ".join(str(p) for p in parts).strip()
            if text:
                on_info(text)

    # make_engine cria o próprio logger com runtime.ConsoleLog: passa a ser o nosso também.
    runtime.ConsoleLog = EventLog

    original_save = frlgtrade.save_received

    def save_received(engine, run_config, lg):
        """Grava como o motor grava e manda cada recebido novo ao app na hora (no commit)."""
        count = original_save(engine, run_config, lg)
        plan = run_config.plan
        mons = engine.received_mons or ([engine.received_mon] if engine.received_mon else [])
        paths = runtime.received_paths(mons, plan.output_path, plan.output_format, plan.trades)
        for index, path in enumerate(paths):
            if path in state["sent"] or not os.path.exists(path):
                continue
            state["sent"].add(path)
            with open(path, "rb") as f:
                data = f.read()
            emit({"type": "received", "tradeIndex": index,
                  "data": base64.b64encode(data).decode("ascii")})
        return count

    frlgtrade.save_received = save_received

    watch = BeaconWatch()
    logging.getLogger().addHandler(watch)

    def end(outcome, code=None, message=None, retryable=False):
        emit({"type": "end", "outcome": outcome, "code": code, "message": message,
              "retryable": retryable})

    if not cryptomod.HAVE_ZSTD:
        end("failed", "engine_broken", "zstandard ausente no Python embutido")
        return 0

    try:
        profile = configmod.profile_from_overrides(
            ot=params["name"], version=params["game"], trainer_id=(params["tid"], params["sid"]))
        slots = tuple(params["slots"])
        plan = configmod.TradePlan(
            party_paths=tuple(params["party"]), output_path=params["out"], output_size=100,
            output_format="pk3", trade_slot=slots[0], offered_slots=slots, trades=len(slots))
        ldn = configmod.LdnConfig(password=None, phy=params["phy"], keys_path=params["keys"])
        role = configmod.JoinerOptions(live=True)
        run_config = configmod.TradeRunConfig(profile, plan, ldn, role)
    except ValueError as e:
        end("failed", "invalid_request", str(e))
        return 0

    # Só os canais que a placa sintoniza: o ldn.scan aborta no primeiro canal recusado (placa sem
    # 5 GHz), e o teste com o Switch já pula esses canais (beacon_child).
    channels = supported_channels(params["phy"], CHANNELS)
    os.environ["FRLG_CHANNELS"] = ",".join(str(c) for c in channels)
    emit({"type": "debug", "text": f"canais da varredura: {os.environ['FRLG_CHANNELS']}"})

    lg = EventLog(False)
    try:
        engine = frlgtrade.run_live(run_config, lg)
    except KeyboardInterrupt:
        end("cancelled", "cancelled")
        return 0
    except RuntimeError as e:
        text = str(e)
        emit({"type": "debug", "text": text[:4000]})
        code, retryable = join_failure(text)
        if code == "no_network_found" and watch.undecryptable:
            code, retryable = "beacon_not_decrypted", False
        # A causa vem nas linhas de baixo ("LDN join failed after 1 attempt(s):\n<causa>").
        end("failed", code, " ".join(text.split())[:300], retryable)
        return 0
    except Exception as e:  # noqa: BLE001 - qualquer outra falha do motor vira erro legível
        emit({"type": "debug", "text": traceback.format_exc()[-4000:]})
        end("failed", "engine_error", f"{type(e).__name__}: {e}"[:300], not state["joined"])
        return 0

    save_received(engine, run_config, lg)
    received = len(state["sent"])
    trades = len(slots)
    if received >= trades:
        end("completed")
    elif received:
        end("failed", "trade_incomplete", f"{received}/{trades}", False)
    elif state["rejected"]:
        end("failed", "join_rejected", None, False)
    elif state["cancelled_by_host"]:
        end("failed", "host_cancelled", None, False)
    elif state["joined"]:
        end("failed", "connection_lost", None, False)
    else:
        end("failed", "join_failed", None, True)
    return 0
