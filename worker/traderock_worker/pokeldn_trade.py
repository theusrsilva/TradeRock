"""Uma sessão de troca com um jogo do Switch pelo pokeldn (D21, D23). O TradeRock é sempre o anfitrião: o console
procura com o código de troca e entra na sala que o app anuncia com esse código.

Um processo por sessão: o pokeldn guarda estado global (o contador de trocas de `pokeldn.ldn`, o
`TRADE_IN_PROGRESS` do Let's Go). O comando é o script `bin/<jogo>_host.py` do pokeldn com as flags testadas do
catálogo dele (`pokeldn.app.command.build`), carregado com importlib e chamado com `main(argv)`.

Entrada (uma linha JSON): {"game": "sv", "offers": ["<base64>", ...], "code": "12345678" | null,
"tradeBack": false, "trainer": {"name", "tid", "sid", "language", "gender"}, "keys": "<caminho do prod.keys>",
"radio": "esp32:COM3" | "lan" | null (placa Wi-Fi, como root), "seconds": 900}.
Depois dessa linha, a entrada fica aberta: a linha "stop" pede a saída educada (o mesmo que o Ctrl+C: o pokeldn passa
a sala ao console antes de fechar), e serve no Windows, onde o app não consegue mandar SIGINT (D30). O fim da entrada
não interrompe nada (o worker fecha a entrada logo depois do pedido).
"lan": modo LAN do Sword/Shield pela rede de casa, sem rádio nem prod.keys (D24).
As ofertas já vêm no formato que o pokeldn espera de cada jogo (PokeldnOffer no C#).

Outras atividades (D26): {"activity": "gift", "game": "frlg" | "swsh", "gift": {"mode": "preset", "preset": key} |
{"mode": "event", "event": key} | {"mode": "file", "data": "<base64>", "extension": "wc3" | "wc8"}, ...} no lugar de
"offers". As chaves dos presentes vêm de catalog/gifts.json (scripts/pokeldn/gift_catalog.py). Nada é gravado
na placa (--save-to-board fica desligado).

Raids (D26): {"activity": "raid", "game": "swsh" | "sv", "raid": {"kind": ..., ...}, ...}. Os tipos e os campos estão
em RAIDS; os Pokémon vão em base64 (.pk8 ou .pk9) e as raids de evento vêm da cópia embutida das galerias
(pokeldn_env.use_bundled_raid_events extrai só o evento escolhido). No Sword/Shield, "radio": "lan" usa as
ferramentas do modo LAN.

Union Circle do Scarlet/Violet (D26): {"activity": "circle", "game": "sv", "circle": {"kind": "quests", "code": "1234",
"task": "snacksworth" | "bp", "bp": 1000} | {"kind": "encounters", "code": "1234", "spawn": {"species": 25, "level": 30,
"shiny": false, "tera": 0-17 | null}}}. O console abre o círculo e o TradeRock entra.
No FRLG, "offers" é o time do anfitrião: primeiro os oferecidos, na ordem das trocas, depois os de preenchimento;
"trades" diz quantos são oferecidos e "version" é "firered" ou "leafgreen".

Saída (linhas JSON num descritor próprio):
- {"type": "event", "kind": "waiting" | "joined" | "left"}: a sala aberta, o console entrou, o console saiu. Vêm das
  linhas do anfitrião do pokeldn (CONSOLE JOINED / console left; no modo LAN, seated / left the session), não do
  "[link]" do ESP32, que também anuncia o próprio enlace da placa quando a sala sobe (D30).
- {"type": "trade_done", "index": n, "data": "<base64 do recebido>"}: o pokeldn grava o recebido na pasta de
  recebidos; o Legends: Arceus e o Z-A gravam DEPOIS da linha "[done]", então o processo espera o arquivo
  (RECEIVED_WAIT) antes de mandar a mensagem. "data" nulo só se o arquivo não aparecer.
- {"type": "done", "kind": "delivery", "index": n} (presente do FRLG entregue)
- {"type": "milestone", "kind": "advertising" | "served", "count": n} (presente do SwSh: o console só ouve o anúncio)
- {"type": "milestone", "kind": "lobby" | "battle" | "over"} (raids: sala aberta, batalha começou, terminou)
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
import threading
import time

from . import pokeldn_env

GAMES = ("frlg", "lgpe", "swsh", "bdsp", "pla", "sv", "za")
EXTENSIONS = {"frlg": "pk3", "lgpe": "pb7", "swsh": "pk8", "bdsp": "pb8", "pla": "pa8", "sv": "pk9", "za": "pa9"}
DONE = re.compile(r"\[done\] (\w+) (\d+) complete")
ACTIVITIES = {"gift": ("frlg", "swsh"), "raid": ("swsh", "sv"), "circle": ("sv",)}
CIRCLES = {"quests": "sv-circle-join", "encounters": "sv-circle-encounters-join"}
BP_MAX = 9_999_999
# Tipo de raid → (jogo, ferramenta do catálogo do pokeldn, a do modo LAN ou None).
RAIDS = {
    "max-raid-host": ("swsh", "swsh-raid-host", "swsh-lan-raid-host"),
    "max-raid-join": ("swsh", "swsh-raid-join", "swsh-lan-raid-join"),
    "lair-host": ("swsh", "swsh-lair-raid-host", "swsh-lan-lair-host"),
    "lair-join": ("swsh", "swsh-lair-raid-join", "swsh-lan-lair-join"),
    "tera-raid-host": ("sv", "sv-raid-host", None),
    "tera-raid-join": ("sv", "sv-raid-join", None),
}
LAIR_LEGENDARIES = (144, 145, 146, 150, 243, 244, 245, 249, 250, 380, 381, 382)
SV_CHOICES = {"version": ("scarlet", "violet"), "map": ("paldea", "kitakami", "blueberry"),
              "progress": ("beginning", "tera", "3star", "4star", "5star", "6star"), "content": ("standard", "black")}
MAX_POKEMON = 512
GIFT_EXTENSIONS = {"frlg": ("wc3", "pokegift"), "swsh": ("wc8", "pokegift")}
MILESTONES = ((re.compile(r"\badvertising comm id\b"), "advertising"),
              (re.compile(r"\bserved (\d+) advertisements\b"), "served"),
              # Raids: as linhas de swsh_host, swsh_connect, sv_host e sv_join (docs/swsh_raid.md, docs/sv_raid.md).
              (re.compile(r"\[(?:raid|lair)\] \S+: lobby opens\b|\bseated at LDN node\b"
                          r"|the host acknowledged the join request"), "lobby"),
              (re.compile(r"both sides started|THE HOST STARTS THE BATTLE|the adventure has started"
                          r"|the battle has begun|\[sv\] the battle begins"
                          # Union Circle: o círculo começou (sv_join.py)
                          r"|\[sv\] -> \S+: circle\b|\[sv\] circle: our player follows"), "battle"),
              (re.compile(r"THE BATTLE IS OVER|our Pokemon is in the battle; closing|our player has left the raid"
                          r"|handing the session to the console|the network is theirs"
                          r"|the circle task is done"), "over"))
# A sala: "[link] waiting" é do ESP32 (pokeldn.ldn.show_link); a placa Wi-Fi e o modo LAN não imprimem "[link]".
WAITING = re.compile(r"\[link\] waiting\b|^Hosting\. Waiting for the console|\[lan\] session 0x")
# O console entrou: o anfitrião IP (ldn_mitm_host.py), o do Let's Go (lgpe_host.py) e o do modo LAN (pia_lan.py).
JOINED = re.compile(r"\*\*\* CONSOLE JOINED \*\*\*|\*\*\* THE CONSOLE JOINED THE MESH \*\*\*"
                    r"|\[lan\] \S+ host request; seated at")
LEFT = re.compile(r"\[host\] console left:|\[lan\] \S+ left the session|\[link\] left\b")
# Quanto esperar o arquivo do recebido depois do "[done]" (o Z-A e o Arceus gravam na volta do laço de rede).
RECEIVED_WAIT = 15.0


class Emitter:
    def __init__(self, fd):
        self._out = os.fdopen(fd, "w", buffering=1, encoding="utf-8")
        self._lock = threading.Lock()

    def __call__(self, obj):
        with self._lock:
            self._out.write(json.dumps(obj, ensure_ascii=False) + "\n")
            self._out.flush()


class Received:
    """Os arquivos que o pokeldn grava na pasta de recebidos: a cada troca concluída, o mais novo ainda não enviado.
    O Sword/Shield e o BDSP gravam quando o console mostra a oferta (antes de concluir) e regravam se ele troca de
    ideia; por isso só vale o que estiver lá no "[done]" (D21). O Legends: Arceus e o Z-A gravam logo DEPOIS do
    "[done]" (bin/pla_host.py e bin/za_host.py: o `show_done()` sai de dentro do `receive()`, e o arquivo é gravado
    quando ele volta), então quem lê espera um pouco (Tracker, D30)."""

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


class Tracker:
    """As linhas do pokeldn → mensagens para o app. A sala ("waiting") sai uma vez; "joined" e "left" seguem a
    presença do console. No "[done] trade N", o recebido é lido da pasta; se ainda não estiver lá, um fio espera
    até RECEIVED_WAIT e manda a mensagem assim que o arquivo aparecer (D30)."""

    def __init__(self, emit, received, wait=RECEIVED_WAIT):
        self.emit = emit
        self.received = received
        self.wait = wait
        self.trades = 0
        self.waited = False
        self.present = False
        self._pending = []          # índices de troca cujo arquivo ainda não apareceu
        self._lock = threading.RLock()
        self._thread = None

    def line(self, line):
        if WAITING.search(line):
            if not self.waited:
                self.waited = True
                self.emit({"type": "event", "kind": "waiting"})
        elif JOINED.search(line):
            if not self.present:
                self.present = True
                self.emit({"type": "event", "kind": "joined"})
        elif LEFT.search(line):
            # "[link] left" também é o enlace da placa caindo no fim: só conta com um console presente.
            if self.present:
                self.present = False
                self.emit({"type": "event", "kind": "left"})
        elif (m := DONE.search(line)) and m.group(1) == "trade":
            self.trades = int(m.group(2))
            with self._lock:
                self._pending.append(self.trades)
                if not self._flush():
                    self._watch()
        elif m:
            self.emit({"type": "done", "kind": m.group(1), "index": int(m.group(2))})
        else:
            for pattern, kind in MILESTONES:
                if found := pattern.search(line):
                    self.emit({"type": "milestone", "kind": kind,
                               "count": int(found.group(1)) if found.groups() else None})

    def _flush(self):
        """Manda o recebido de cada troca pendente que já tem arquivo. -> True sem pendência."""
        with self._lock:
            while self._pending:
                data = self.received.take()
                if data is None:
                    return False
                self.emit({"type": "trade_done", "index": self._pending.pop(0),
                           "data": base64.b64encode(data).decode()})
            return True

    def _watch(self):
        if self._thread is not None and self._thread.is_alive():
            return
        deadline = time.monotonic() + self.wait

        def run():
            while time.monotonic() < deadline:
                time.sleep(0.05)
                if self._flush():
                    return
            self._give_up()

        self._thread = threading.Thread(target=run, name="traderock-received", daemon=True)
        self._thread.start()

    def _give_up(self):
        with self._lock:
            if self._flush():
                return
            for index in self._pending:
                self.emit({"type": "trade_done", "index": index, "data": None})
            self._pending.clear()

    def finish(self):
        """No fim do processo: o que ainda não apareceu não vai mais aparecer."""
        thread = self._thread
        if thread is not None and thread.is_alive():
            thread.join(self.wait)
        self._give_up()


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
        if activity == "raid":
            validate_raid(game, params.get("raid"), params.get("radio") == "lan")
        elif activity == "circle":
            validate_circle(params.get("circle"))
        else:
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
        if game != "swsh" or params.get("activity") not in (None, "raid"):
            raise ValueError("o modo LAN é só da troca e das raids do Sword/Shield")
        return False
    if radio is not None and not re.fullmatch(r"esp32:(/dev/[A-Za-z0-9_.\-/]+|COM[0-9]{1,3})", str(radio)):
        raise ValueError("rádio inválido")
    if not isinstance(params.get("keys"), str) or not os.path.isfile(params["keys"]):
        raise ValueError("prod.keys ausente")
    return True


def _pokemon(value, required):
    if value is None and not required:
        return
    if not isinstance(value, str) or not 0 < len(base64.b64decode(value, validate=True)) <= MAX_POKEMON:
        raise ValueError("Pokémon inválido")


def validate_raid(game, raid, lan):
    if not isinstance(raid, dict) or raid.get("kind") not in RAIDS:
        raise ValueError("raid inválida")
    raid_game, _, lan_tool = RAIDS[raid["kind"]]
    if raid_game != game or (lan and lan_tool is None):
        raise ValueError("raid inválida para este jogo")
    event = raid.get("event")
    # A chave é a pasta do evento dentro da galeria ("Index 001", "053 Evento/Round 2"); command() confere se existe.
    if event is not None and (not isinstance(event, str) or not 0 < len(event) <= 200 or not event.isprintable()
                              or ".." in event.split("/") or event.startswith("/") or "\\" in event):
        raise ValueError("evento inválido")
    kind = raid["kind"]
    if kind == "max-raid-host":
        _pokemon(raid.get("offer"), True)
        if event is None:
            _pokemon(raid.get("boss"), True)
        elif not re.fullmatch(r"(sword|shield):[1-5]:\d{1,2}:(Wild Area|Isle of Armor|Crown Tundra)", str(raid.get("eventBoss"))):
            raise ValueError("chefe inválido")
    elif kind == "max-raid-join":
        _pokemon(raid.get("offer"), False)
        if raid.get("move", 1) not in (1, 2, 3, 4, "auto"):
            raise ValueError("golpe inválido")
        for name in ("targetTypes", "attackerTypes"):
            types = raid.get(name)
            if types is not None and (not isinstance(types, list) or not 1 <= len(types) <= 2
                                      or not all(isinstance(t, int) and 0 <= t <= 17 for t in types)):
                raise ValueError("tipos inválidos")
    elif kind == "lair-host":
        if raid.get("legendary") not in LAIR_LEGENDARIES:
            raise ValueError("lendário inválido")
        if raid.get("code") is not None and not re.fullmatch(r"\d{8}", str(raid["code"])):
            raise ValueError("código inválido")
    elif kind == "tera-raid-host":
        _pokemon(raid.get("pokemon"), True)
        for name, allowed in SV_CHOICES.items():
            if raid.get(name) is not None and raid[name] not in allowed:
                raise ValueError(f"{name} inválido")
        if not re.fullmatch(r"[0-9A-Fa-f]{8}", str(raid.get("seed", "000F34C3"))):
            raise ValueError("semente inválida")
        if raid.get("group") is not None and raid["group"] not in range(1, 11):
            raise ValueError("grupo inválido")
        if raid.get("players", 1) not in (1, 2, 3):
            raise ValueError("jogadores inválidos")
    elif kind == "tera-raid-join":
        _pokemon(raid.get("pokemon"), True)


def validate_circle(circle):
    if not isinstance(circle, dict) or circle.get("kind") not in CIRCLES:
        raise ValueError("Union Circle inválido")
    if not re.fullmatch(r"\d{4}", str(circle.get("code"))):
        raise ValueError("código inválido")
    if circle["kind"] == "quests":
        if circle.get("task", "snacksworth") not in ("snacksworth", "bp"):
            raise ValueError("tarefa inválida")
        if circle.get("task") == "bp" and not (isinstance(circle.get("bp"), int) and 1 <= circle["bp"] <= BP_MAX):
            raise ValueError("BP inválido")
        return
    spawn = circle.get("spawn")
    if not isinstance(spawn, dict) or not isinstance(spawn.get("species"), int) or not 1 <= spawn["species"] <= 1025:
        raise ValueError("espécie inválida")
    if not isinstance(spawn.get("level"), int) or not 1 <= spawn["level"] <= 100:
        raise ValueError("nível inválido")
    if spawn.get("tera") is not None and spawn["tera"] not in range(18):
        raise ValueError("tipo Tera inválido")


def circle_values(tool, circle):
    values = {}
    for field in tool.fields:
        flag = field.flag[0] if isinstance(field.flag, tuple) else field.flag
        if flag == "--code":
            values[field.key] = circle["code"]
        elif flag == "--circle-task":
            values[field.key] = circle.get("task", "snacksworth")
        elif flag == "--bbq-clear" and circle.get("bp"):
            values[field.key] = str(circle["bp"])
        elif flag == "--spawn":
            spawn = circle["spawn"]
            # Sem "legal": a conferência do lugar pede o ajudante PKHeX do pokeldn (D26).
            value = {"species": spawn["species"], "level": spawn["level"], "shiny": bool(spawn.get("shiny"))}
            if spawn.get("tera") is not None:
                value["tera"] = spawn["tera"]
            values[field.key] = value
    return values


def raid_values(tool, raid, game, work):
    """Valores dos campos da ferramenta de raid, pela flag (o pokeldn monta os argumentos com eles)."""
    extension = EXTENSIONS[game]

    def pokemon(name):
        if raid.get(name) is None:
            return []
        path = os.path.join(work, f"{name}.{extension}")
        with open(path, "wb") as f:
            f.write(base64.b64decode(raid[name]))
        return [{"file": path}]

    wanted = {
        "--raid-event": raid.get("event") or "",
        "--raid-version": raid.get("eventBoss") if game == "swsh" else raid.get("version"),
        "--raid-boss": pokemon("boss"),
        "--raid-gigantamax": bool(raid.get("gigantamax")),
        "--offer-file": pokemon("offer"),
        "--raid-move": str(raid_move(raid)),
        "--lair-legendary": str(raid.get("legendary", "")),
        "--code": raid.get("code") or "",
        "--raid-pokemon": pokemon("pokemon"),
        "--raid-map": raid.get("map"),
        "--raid-progress": raid.get("progress"),
        "--raid-content": raid.get("content"),
        "--raid-event-group": str(raid["group"]) if raid.get("group") else "",
        "--raid-seed": (raid.get("seed") or "000F34C3").upper(),
        "--raid-players": str(raid.get("players", 1)),
    }
    values = {}
    for field in tool.fields:
        flag = field.flag[0] if isinstance(field.flag, tuple) else field.flag
        if flag in wanted and wanted[flag] is not None:
            values[field.key] = wanted[flag]
    return values


def raid_move(raid):
    """O golpe da Max Raid em que o TradeRock entra: o escolhido, ou "auto" = o mais efetivo contra o chefe
    informado (o mais forte sem chefe) pela mini IA (D31). Sem Pokémon próprio, o golpe 1."""
    move = raid.get("move", 1)
    if move != "auto":
        return move
    if not raid.get("offer"):
        return 1
    from pokeldn.swsh import pokemon as swsh_pokemon
    from . import partner_ai
    try:
        move_list = [m for m in swsh_pokemon.read(base64.b64decode(raid["offer"]))["moves"] if m]
    except Exception:  # noqa: BLE001 - Pokémon ilegível: fica o golpe 1, como antes
        return 1
    targets = [raid["targetTypes"]] if raid.get("targetTypes") else [[]]
    slot = partner_ai.best_slot(move_list, targets, tuple(raid.get("attackerTypes") or ()))
    print(f"[ai] Max Raid: golpe {slot + 1} de {move_list}", file=sys.stderr)
    return slot + 1


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
    elif activity == "raid":
        _, key, lan_key = RAIDS[params["raid"]["kind"]]
        key = lan_key if lan else key
    elif activity == "circle":
        key = CIRCLES[params["circle"]["kind"]]
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
    if activity == "circle":
        return os.path.join(engine, tool.script), build(tool, circle_values(tool, params["circle"]), extra, settings), received
    if activity == "raid":
        event = params["raid"].get("event")
        if event:
            from pokeldn.app import events_gallery
            if not pokeldn_env.use_bundled_raid_events(engine, work, game, event) or not events_gallery.holds(event, game=game):
                raise ValueError("evento desconhecido")
        values = raid_values(tool, params["raid"], game, work)
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
        tracker = Tracker(emit, Received(received_dir))
        # Dynamax Adventure com o TradeRock entrando: a mini IA escolhe o aluguel e os golpes (D31).
        raid = params.get("raid") or {}
        if params.get("activity") == "raid" and raid.get("kind") == "lair-join" and raid.get("ai", True):
            from . import partner_ai
            partner_ai.install_lair(lambda line: print(line, flush=True))
        watch_stop(sys.stdin)

        emit({"type": "event", "kind": "starting", "game": game})
        sys.stdout = Lines(tracker.line, log)
        try:
            code = run_script(script, argv)
        except KeyboardInterrupt:
            code = 130
        except Exception as e:  # noqa: BLE001 - vira código para a tela; detalhes no log
            sys.stdout = log
            log.write(pokeldn_env.redact(f"{type(e).__name__}: {e}") + "\n")
            from .pokeldn_board import classify
            code = classify(e)
            # O que o console já mandou fica salvo mesmo com o erro: nunca perder um recebido (D30).
            tracker.finish()
            emit({"type": "error", "code": code if code.startswith("serial_") else "engine_error",
                  "message": pokeldn_env.redact(str(e))})
            return 1
        finally:
            sys.stdout.flush()
            sys.stdout = log
        tracker.finish()
        emit({"type": "result", "exitCode": code, "trades": tracker.trades, "game": game})
        return 0 if code in (0, 130) else 1


def watch_stop(stream):
    """A linha "stop" na entrada vira um KeyboardInterrupt no fio principal, como o Ctrl+C: o pokeldn passa a sala
    ao console e sai. O fim da entrada não faz nada (o worker fecha a entrada logo depois do pedido)."""
    def run():
        try:
            for line in stream:
                if line.strip() == "stop":
                    import _thread
                    _thread.interrupt_main()
                    return
        except (OSError, ValueError):
            pass

    threading.Thread(target=run, name="traderock-stop", daemon=True).start()
