"""Uma sessão de troca com um jogo do Switch pelo pokeldn (D21, D23). O TradeRock é sempre o anfitrião: o console
procura com o código de troca e entra na sala que o app anuncia com esse código.

Um processo por sessão: o pokeldn guarda estado global (o contador de trocas de `pokeldn.ldn`, o
`TRADE_IN_PROGRESS` do Let's Go). O comando é o script `bin/<jogo>_host.py` do pokeldn com as flags testadas do
catálogo dele (`pokeldn.app.command.build`), carregado com importlib e chamado com `main(argv)`.

Entrada (uma linha JSON): {"game": "sv", "offers": ["<base64>", ...], "code": "12345678" | null,
"tradeBack": false, "trainer": {"name", "tid", "sid", "language", "gender"}, "keys": "<caminho do prod.keys>",
"radio": "esp32:COM3" | null (placa Wi-Fi, como root), "seconds": 900}.
As ofertas já vêm no formato que o pokeldn espera de cada jogo (PokeldnOffer no C#).
No FRLG, "offers" é o time do anfitrião: primeiro os oferecidos, na ordem das trocas, depois os de preenchimento;
"trades" diz quantos são oferecidos e "version" é "firered" ou "leafgreen".

Saída (linhas JSON num descritor próprio):
- {"type": "event", "kind": "waiting" | "joined" | "left"}
- {"type": "trade_done", "index": n, "data": "<base64 do recebido>"}
- {"type": "result", "exitCode": n, "trades": n} ou {"type": "error", "code", "message"}
O `print` do pokeldn vai para a saída de erro (log), com as sequências hexadecimais longas apagadas.
"""

import base64
import importlib.util
import inspect
import io
import json
import os
import re
import signal
import sys
import tempfile

from . import pokeldn_env

GAMES = ("frlg", "lgpe", "swsh", "bdsp", "pla", "sv", "za")
EXTENSIONS = {"frlg": "pk3", "lgpe": "pb7", "swsh": "pk8", "bdsp": "pb8", "pla": "pa8", "sv": "pk9", "za": "pa9"}
DONE = re.compile(r"\[done\] trade (\d+) complete")
LINK = re.compile(r"\[link\] (waiting|joined|left)\b")


class Emitter:
    def __init__(self, fd):
        self._out = os.fdopen(fd, "w", buffering=1, encoding="utf-8")

    def __call__(self, obj):
        self._out.write(json.dumps(obj, ensure_ascii=False) + "\n")
        self._out.flush()


class Received:
    """Os arquivos que o pokeldn grava na pasta de recebidos: a cada troca concluída, o mais novo ainda não enviado.
    O Sword/Shield e o BDSP gravam quando o console mostra a oferta (antes de concluir) e regravam se ele troca de
    ideia; por isso só vale o que estiver lá no "[done]" (D21)."""

    def __init__(self, folder):
        self.folder = folder
        self.sent = set()

    def take(self):
        files = [os.path.join(self.folder, f) for f in os.listdir(self.folder)]
        files = [f for f in files if os.path.isfile(f) and f not in self.sent]
        if not files:
            return None
        newest = max(files, key=os.path.getmtime)
        self.sent.add(newest)
        with open(newest, "rb") as f:
            return f.read()


class Lines(io.TextIOBase):
    """`sys.stdout` do pokeldn: cada linha vira evento (quando é um marco) e vai para o log, sem chaves."""

    def __init__(self, on_line, log):
        self._buffer = ""
        self._on_line = on_line
        self._log = log

    def writable(self):
        return True

    def write(self, text):
        self._buffer += text
        while "\n" in self._buffer:
            line, self._buffer = self._buffer.split("\n", 1)
            self._log.write(pokeldn_env.redact(line) + "\n")
            self._on_line(line)
        return len(text)

    def flush(self):
        self._log.flush()


def validate(params):
    game = params.get("game")
    if game not in GAMES:
        raise ValueError("jogo inválido")
    offers = params.get("offers")
    if not isinstance(offers, list) or not 1 <= len(offers) <= 6:
        raise ValueError("ofertas inválidas")
    code = params.get("code")
    if code is not None and not re.fullmatch(r"\d{8}|\d,\d,\d", str(code)):
        raise ValueError("código inválido")
    radio = params.get("radio")
    if radio is not None and not re.fullmatch(r"esp32:(/dev/[A-Za-z0-9_.\-/]+|COM[0-9]{1,3})", str(radio)):
        raise ValueError("rádio inválido")
    if not isinstance(params.get("keys"), str) or not os.path.isfile(params["keys"]):
        raise ValueError("prod.keys ausente")
    if game == "frlg":
        trades = params.get("trades")
        if not isinstance(trades, int) or not 1 <= trades <= len(offers):
            raise ValueError("trocas inválidas")
        if params.get("version") not in ("firered", "leafgreen"):
            raise ValueError("versão inválida")
    return game


def lgpe_code(code):
    """"0,4,9" → os nomes da grade do pokeldn (pokeldn/lgpe/session.py CODE_PICKER)."""
    from pokeldn.lgpe.session import CODE_PICKER
    return ",".join(CODE_PICKER[int(i)] for i in code.split(","))


def command(params, engine, work):
    """Argumentos do script de anfitrião do jogo, montados pelo catálogo do pokeldn (flags testadas)."""
    from pokeldn.app.catalog import GAMES as CATALOG
    from pokeldn.app.command import build
    from pokeldn.app.settings import Settings

    game = params["game"]
    key = "frlg-trade-host" if game == "frlg" else f"{game}-host"
    tool = next(t for g in CATALOG for t in g.tools if t.key == key)
    trainer = params.get("trainer") or {}
    received = os.path.join(work, "received")
    os.makedirs(received, exist_ok=True)
    gen3 = game == "frlg"
    settings = Settings(
        keys=params["keys"], received=received, capture=False, sprites=False, check_updates=False,
        ot=trainer.get("name") or "TRADEROCK", language=int(trainer.get("language", 2)),
        gender=int(trainer.get("gender", 0)),
        tid=int(trainer.get("tid", 0)) if gen3 else 0, sid=int(trainer.get("sid", 0)) if gen3 else 0,
        switch_tid=None if gen3 else int(trainer.get("tid", 0)), switch_sid=None if gen3 else int(trainer.get("sid", 0)))

    files = []
    for i, data in enumerate(params["offers"]):
        path = os.path.join(work, f"offer-{i + 1}.{EXTENSIONS[game]}")
        with open(path, "wb") as f:
            f.write(base64.b64decode(data))
        files.append({"file": path})
    values = {}
    for field in tool.fields:
        if field.kind == "pokemon":
            values[field.key] = files
        elif field.kind in ("code", "linkcode") and params.get("code"):
            values[field.key] = lgpe_code(params["code"]) if field.kind == "linkcode" else params["code"]
        elif field.key == "--fresh-pid":
            # O PID novo passa pelo ajudante PKHeX do pokeldn, que o TradeRock não leva (D21).
            values[field.key] = False
        elif field.key == "--trade-back":
            values[field.key] = bool(params.get("tradeBack"))
        elif field.key == "--seconds" and params.get("seconds"):
            values[field.key] = str(int(params["seconds"]))
    # FRLG: o anfitrião oferece as posições 0..n-1 do time (--slot 0 do catálogo); o resto é preenchimento.
    extra = {"--trades": params["trades"], "--version": params["version"]} if gen3 else {}
    return os.path.join(engine, tool.script), build(tool, values, extra, settings), received


def run_script(script, argv):
    spec = importlib.util.spec_from_file_location("traderock_pokeldn_tool", script)
    module = importlib.util.module_from_spec(spec)
    sys.argv = [script, *argv]  # o main() do pla_host lê sys.argv
    spec.loader.exec_module(module)
    try:
        result = module.main(argv) if inspect.signature(module.main).parameters else module.main()
    except SystemExit as e:
        result = e.code
    return result if isinstance(result, int) else 0 if result in (None, True) else 1


def main(root):
    emit = Emitter(os.dup(1))
    log = sys.stderr
    os.dup2(2, 1)
    signal.signal(signal.SIGINT, signal.default_int_handler)  # o pai ignora SIGINT; o filho sai com educação
    try:
        params = json.loads(sys.stdin.readline())
        game = validate(params)
    except (ValueError, KeyError) as e:
        emit({"type": "error", "code": "invalid", "message": str(e)})
        return 2

    with tempfile.TemporaryDirectory(prefix="traderock-pokeldn-") as work:
        engine = pokeldn_env.prepare(root, os.path.join(work, "data"), params.get("radio"))
        script, argv, received_dir = command(params, engine, work)
        received = Received(received_dir)
        state = {"trades": 0}

        def on_line(line):
            if m := LINK.search(line):
                emit({"type": "event", "kind": m.group(1)})
            elif m := DONE.search(line):
                state["trades"] = int(m.group(1))
                data = received.take()
                emit({"type": "trade_done", "index": state["trades"],
                      "data": base64.b64encode(data).decode() if data else None})

        emit({"type": "event", "kind": "starting", "game": game})
        sys.stdout = Lines(on_line, log)
        try:
            code = run_script(script, argv)
        except KeyboardInterrupt:
            code = 130
        except Exception as e:  # noqa: BLE001 - vira código para a tela; detalhes no log
            sys.stdout = log
            log.write(pokeldn_env.redact(f"{type(e).__name__}: {e}") + "\n")
            from .pokeldn_board import classify
            code = classify(e)
            emit({"type": "error", "code": code if code.startswith("serial_") else "engine_error",
                  "message": pokeldn_env.redact(str(e))})
            return 1
        finally:
            sys.stdout.flush()
            sys.stdout = log
        emit({"type": "result", "exitCode": code, "trades": state["trades"], "game": game})
        return 0 if code in (0, 130) else 1
