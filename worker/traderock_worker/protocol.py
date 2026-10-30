"""Validação estrita de tudo o que chega do app. O worker roda como root: nada é aceito sem conferir
formato e tamanho, e nenhum caminho de arquivo vem de fora (os arquivos são enviados como bytes)."""

import base64
import binascii
import re
import json
from dataclasses import dataclass

MAX_LINE = 1 << 20          # 1 MiB por mensagem
MAX_KEYS = 64 * 1024        # prod.keys tem poucos KB
PHY_RE = re.compile(r"^phy[0-9]{1,3}$")
GAMES = ("firered", "leafgreen")


class RequestError(Exception):
    """Pedido inválido. `code` é estável (o app traduz)."""

    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def phy(value):
    if not isinstance(value, str) or not PHY_RE.match(value):
        raise RequestError("invalid_phy", "phy deve ser algo como phy0")
    return value


def _b64(value, what, max_size):
    if not isinstance(value, str) or len(value) > max_size * 2:
        raise RequestError("invalid_request", f"{what}: base64 esperado")
    try:
        data = base64.b64decode(value, validate=True)
    except (binascii.Error, ValueError):
        raise RequestError("invalid_request", f"{what}: base64 inválido") from None
    if len(data) > max_size:
        raise RequestError("invalid_request", f"{what}: grande demais")
    return data


def _int(value, what, lo, hi):
    if type(value) is not int or not lo <= value <= hi:
        raise RequestError("invalid_request", f"{what} deve ser um inteiro entre {lo} e {hi}")
    return value


@dataclass(frozen=True)
class TradeParams:
    phy: str
    keys: bytes
    party: tuple
    slots: tuple
    game: str
    name: str
    tid: int
    sid: int | None
    approval_seconds: int


def trade_params(msg):
    keys = _b64(msg.get("keys"), "keys", MAX_KEYS)
    if not keys:
        raise RequestError("invalid_request", "keys vazio")

    party = msg.get("party")
    if not isinstance(party, list) or not 2 <= len(party) <= 6:
        raise RequestError("invalid_request", "party deve ter de 2 a 6 Pokémon")
    mons = tuple(_b64(p, "party", 100) for p in party)
    if any(len(m) not in (80, 100) for m in mons):
        raise RequestError("invalid_request", "cada Pokémon deve ter 80 ou 100 bytes")

    slots = msg.get("slots")
    if not isinstance(slots, list) or not 1 <= len(slots) <= len(mons):
        raise RequestError("invalid_request", "slots deve ter de 1 até o tamanho do time")
    slots = tuple(_int(s, "slot", 0, len(mons) - 1) for s in slots)
    if len(set(slots)) != len(slots):
        raise RequestError("invalid_request", "slots repetidos")

    game = msg.get("game")
    if game not in GAMES:
        raise RequestError("invalid_request", "game deve ser firered ou leafgreen")

    name = msg.get("name")
    if (not isinstance(name, str) or not 1 <= len(name) <= 7
            or any(not ch.isprintable() for ch in name)):
        raise RequestError("invalid_request", "nome do treinador inválido")

    sid = msg.get("sid")
    return TradeParams(
        phy=phy(msg.get("phy")),
        keys=keys,
        party=mons,
        slots=slots,
        game=game,
        name=name,
        tid=_int(msg.get("tid"), "tid", 0, 0xFFFF),
        sid=None if sid is None else _int(sid, "sid", 0, 0xFFFF),
        approval_seconds=_int(msg.get("approvalSeconds", 60), "approvalSeconds", 10, 900),
    )


@dataclass(frozen=True)
class BeaconParams:
    phy: str
    keys: bytes
    seconds: int


def beacon_params(msg):
    keys = _b64(msg.get("keys"), "keys", MAX_KEYS)
    if not keys:
        raise RequestError("invalid_request", "keys vazio")
    return BeaconParams(
        phy=phy(msg.get("phy")),
        keys=keys,
        seconds=_int(msg.get("seconds", 30), "seconds", 5, 120),
    )


@dataclass(frozen=True)
class PokeldnParams:
    phy: str
    keys: bytes
    request: dict


def pokeldn_params(msg):
    """Troca pelo pokeldn com a placa Wi-Fi (D23). O pedido do jogo é conferido pelo pokeldn_trade.validate no filho;
    aqui só o que é do worker: a placa e as chaves."""
    keys = _b64(msg.get("keys"), "keys", MAX_KEYS)
    if not keys:
        raise RequestError("invalid_request", "keys vazio")
    request = msg.get("request")
    if not isinstance(request, dict):
        raise RequestError("invalid_request", "request ausente")
    if len(json.dumps(request)) > MAX_LINE:
        raise RequestError("invalid_request", "request grande demais")
    return PokeldnParams(phy=phy(msg.get("phy")), keys=keys, request=request)
