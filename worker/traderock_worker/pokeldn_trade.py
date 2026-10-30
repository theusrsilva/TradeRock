"""Uma sessão de troca com um jogo do Switch pelo pokeldn (D21, D23). O TradeRock é sempre o anfitrião: o console
procura com o código de troca e entra na sala que o app anuncia com esse código.

Um processo por sessão: o pokeldn guarda estado global (o contador de trocas de `pokeldn.ldn`, o
`TRADE_IN_PROGRESS` do Let's Go). O comando é o script `bin/<jogo>_host.py` do pokeldn com as flags testadas do
catálogo dele (`pokeldn.app.command.build`), carregado com importlib e chamado com `main(argv)`.

Entrada (uma linha JSON): {"game": "sv", "offers": ["<base64>", ...], "code": "12345678" | null,
"tradeBack": false, "trainer": {"name", "tid", "sid", "language", "gender"}, "keys": "<caminho do prod.keys>",
"radio": "esp32:COM3" | "lan" | null (placa Wi-Fi, como root), "seconds": 900}.
"lan": modo LAN do Sword/Shield pela rede de casa, sem rádio nem prod.keys (D24).
As ofertas já vêm no formato que o pokeldn espera de cada jogo (PokeldnOffer no C#).

Outras atividades (D26): {"activity": "gift", "game": "frlg" | "swsh", "gift": {"mode": "preset", "preset": key} |
{"mode": "event", "event": key} | {"mode": "file", "data": "<base64>", "extension": "wc3" | "wc8"}, ...} no lugar de
"offers". As chaves dos presentes vêm de catalog/gifts.json (scripts/pokeldn/gift_catalog.py). Nada é gravado
na placa (--save-to-board fica desligado).
No FRLG, "offers" é o time do anfitrião: primeiro os oferecidos, na ordem das trocas, depois os de preenchimento;
"trades" diz quantos são oferecidos e "version" é "firered" ou "leafgreen".

Saída (linhas JSON num descritor próprio):
- {"type": "event", "kind": "waiting" | "joined" | "left"}
- {"type": "trade_done", "index": n, "data": "<base64 do recebido>"}
- {"type": "done", "kind": "delivery", "index": n} (presente do FRLG entregue)
- {"type": "milestone", "kind": "advertising" | "served", "count": n} (presente do SwSh: o console só ouve o anúncio)
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
DONE = re.compile(r"\[done\] (\w+) (\d+) complete")
ACTIVITIES = {"gift": ("frlg", "swsh")}
GIFT_EXTENSIONS = {"frlg": ("wc3", "pokegift"), "swsh": ("wc8", "pokegift")}
MILESTONES = ((re.compile(r"\badvertising comm id\b"), "advertising"),
              (re.compile(r"\bserved (\d+) advertisements\b"), "served"))
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


def validate_gift(game, gift):
    if not isinstance(gift, dict):
        raise ValueError("presente inválido")
    mode = gift.get("mode")
    key = re.compile(r"[a-z0-9][a-z0-9-]{0,99}")
    if mode == "preset" and isinstance(gift.get("preset"), str) and key.fullmatch(gift["preset"]):
        return
    if mode == "event" and game == "swsh" and isinstance(gift.get("event"), str) and key.fullmatch(gift["event"]):
        return
    if mode == "file" and gift.get("extension") in GIFT_EXTENSIONS[game] and isinstance(gift.get("data"), str):
        if not 0 < len(base64.b64decode(gift["data"], validate=True)) <= 256 << 10:
            raise ValueError("arquivo de presente inválido")
        return
    raise ValueError("presente inválido")


def validate(params):
    game = params.get("game")
    if game not in GAMES:
        raise ValueError("jogo inválido")
    activity = params.get("activity")
    if activity is not None:
        if game not in ACTIVITIES.get(activity, ()):
            raise ValueError("atividade inválida")
        validate_gift(game, params.get("gift"))
        if game == "frlg" and params.get("version") not in ("firered", "leafgreen"):
            raise ValueError("versão inválida")
        validate_radio(params, game)
        return game
    offers = params.get("offers")
    if not isinstance(offers, list) or not 1 <= len(offers) <= 6:
        raise ValueError("ofertas inválidas")
    code = params.get("code")
    if code is not None and not re.fullmatch(r"\d{8}|\d,\d,\d", str(code)):
        raise ValueError("código inválido")
    if not validate_radio(params, game):
        return game
    if game == "frlg":
        trades = params.get("trades")
        if not isinstance(trades, int) or not 1 <= trades <= len(offers):
            raise ValueError("trocas inválidas")
        if params.get("version") not in ("firered", "leafgreen"):
            raise ValueError("versão inválida")
    return game


def validate_radio(params, game):
    """-> False no modo LAN (sem rádio nem chaves)."""
    if params.get("phy") is not None and not re.fullmatch(r"phy[0-9]{1,3}", str(params["phy"])):
        raise ValueError("phy inválido")
    radio = params.get("radio")
    if radio == "lan":
        if game != "swsh" or params.get("activity") is not None:
            raise ValueError("o modo LAN é só da troca do Sword/Shield")
        return False
    if radio is not None and not re.fullmatch(r"esp32:(/dev/[A-Za-z0-9_.\-/]+|COM[0-9]{1,3})", str(radio)):
        raise ValueError("rádio inválido")
    if not isinstance(params.get("keys"), str) or not os.path.isfile(params["keys"]):
        raise ValueError("prod.keys ausente")
    return True


def lgpe_code(code):
    """"0,4,9" → os nomes da grade do pokeldn (pokeldn/lgpe/session.py CODE_PICKER)."""
    from pokeldn.lgpe.session import CODE_PICKER
    return ",".join(CODE_PICKER[int(i)] for i in code.split(","))


def command(params, engine, work):
    """Argumentos do script de anfitrião do jogo, montados pelo catálogo do pokeldn (flags testadas)."""
    from pokeldn.app.catalog import GAMES as CATALOG
    from pokeldn.app.command import build, prepare
    from pokeldn.app.settings import Settings

    game = params["game"]
    lan = params.get("radio") == "lan"
    activity = params.get("activity")
    if activity == "gift":
        key = f"{game}-gift"
    else:
        key = "swsh-lan-host" if lan else "frlg-trade-host" if game == "frlg" else f"{game}-host"
    tool = next(t for g in CATALOG for t in g.tools if t.key == key)
    trainer = params.get("trainer") or {}
    received = os.path.join(work, "received")
    os.makedirs(received, exist_ok=True)
    gen3 = game == "frlg"
    settings = Settings(
        keys=params.get("keys") or os.devnull, received=received, capture=False, sprites=False, check_updates=False,
        ot=trainer.get("name") or "TRADEROCK", language=int(trainer.get("language", 2)),
        gender=int(trainer.get("gender", 0)),
        tid=int(trainer.get("tid", 0)) if gen3 else 0, sid=int(trainer.get("sid", 0)) if gen3 else 0,
        switch_tid=None if gen3 else int(trainer.get("tid", 0)), switch_sid=None if gen3 else int(trainer.get("sid", 0)))

    extra = {"--phy": params["phy"]} if params.get("phy") else {}  # placa Wi-Fi escolhida (launcher.py: --phy)
    if activity == "gift":
        values = {"--gift-file": gift_value(game, params["gift"], work), "--save-to-board": False}
        if game == "frlg":
            values["--version"] = params["version"]
        elif params.get("seconds"):
            values["--seconds"] = str(int(params["seconds"]))
        values = prepare(tool, values)  # grava o .pokegift dos presentes prontos e dos eventos (gift_builder)
        return os.path.join(engine, tool.script), build(tool, values, extra, settings), received

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
    if gen3:
        extra.update({"--trades": params["trades"], "--version": params["version"]})
    return os.path.join(engine, tool.script), build(tool, values, extra, settings), received


def gift_value(game, gift, work):
    """O valor do campo "builder" do pokeldn (pokeldn/app/gift_builder.py): preset, evento oficial ou arquivo.
    O gift_builder troca uma chave desconhecida pela primeira da lista, então a chave é conferida aqui."""
    from pokeldn.app import gift_builder
    builder = gift_builder.module(game)
    if gift["mode"] == "preset":
        preset = builder.PRESET.get(gift["preset"])
        # Os Pokémon de evento do FRLG pedem o ajudante PKHeX do pokeldn; os presets com opções, a interface dele;
        # os que leem o save ficam de fora (D26). O mesmo filtro de scripts/pokeldn/gift_catalog.py.
        if (preset is None or "--event-pokemon" in preset.args or getattr(preset, "arguments", None)
                or preset.group in ("Read the save", "Game boosts")):
            raise ValueError("presente desconhecido")
    if gift["mode"] == "event" and gift["event"] not in builder.OFFICIAL.by_key():
        raise ValueError("evento desconhecido")
    if gift["mode"] == "file":
        path = os.path.join(work, f"gift.{gift['extension']}")
        with open(path, "wb") as f:
            f.write(base64.b64decode(gift["data"]))
        return {"mode": "file", "file": path}
    if gift["mode"] == "event":
        return {"mode": "event", "event": gift["event"]}
    return {"mode": "preset", "preset": gift["preset"]}


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
        try:
            script, argv, received_dir = command(params, engine, work)
        except ValueError as e:
            emit({"type": "error", "code": "invalid", "message": str(e)})
            return 2
        received = Received(received_dir)
        state = {"trades": 0}

        def on_line(line):
            if m := LINK.search(line):
                emit({"type": "event", "kind": m.group(1)})
            elif (m := DONE.search(line)) and m.group(1) == "trade":
                state["trades"] = int(m.group(2))
                data = received.take()
                emit({"type": "trade_done", "index": state["trades"],
                      "data": base64.b64encode(data).decode() if data else None})
            elif m:
                emit({"type": "done", "kind": m.group(1), "index": int(m.group(2))})
            else:
                for pattern, kind in MILESTONES:
                    if found := pattern.search(line):
                        emit({"type": "milestone", "kind": kind,
                              "count": int(found.group(1)) if found.groups() else None})

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
