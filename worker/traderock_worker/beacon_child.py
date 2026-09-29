"""Processo filho (root): teste com o Switch (etapa 6 do assistente).

Com a sala do Direct Corner aberta como Leader, o Switch anuncia a sessão local (LDN) em quadros de
ação criptografados. O teste só ESCUTA esses anúncios, sem entrar na sala, com a própria biblioteca
LDN (a mesma que a troca usa):

1. cria a interface em modo monitor (`wlan.create_factory` → `Factory.create_monitor`);
2. passa pelos canais (`Monitor.set_channel`), escutando um pouco em cada um, enquanto o
   `ldn.Scanner` recebe os quadros e tenta decifrar cada anúncio com as chaves
   (`KeyDerivation`, protocolos LDN 1 e 3);
3. repete as voltas até achar a sala do FireRed/LeafGreen ou até o tempo acabar.

Quando um anúncio LDN não decifra, a biblioteca registra "Failed to parse advertisement frame" no
logger `ldn` e segue em frente: é assim que o teste separa "nenhuma sala" de "sala vista, mas as
chaves não servem" (D5).

A sala do FRLG é reconhecida como o motor reconhece (frlgsim.transport, com os patches):
`LOCAL_COMMUNICATION_ID` + `KNOWN_COMM_IDS`, ou o formato do beacon de busca do RFU.
"""

import json
import logging
import os
import signal
import sys
import time

from .radio import TEST_VIF

# Os mesmos canais e o mesmo tempo por canal da varredura do motor (patch 0001: FRLG_CHANNELS e
# FRLG_SCAN_DWELL), para que "achou no teste" signifique "a troca vai achar".
CHANNELS = (1, 6, 11, 36, 40, 44, 48)
DWELL = 0.6
DEFAULT_SECONDS = 30
# Depois de ver um anúncio que não decifra, mais uma volta completa (a sala do FRLG pode estar em
# outro canal e decifrar) e então para: com chaves erradas, esperar mais não muda nada.
EXTRA_SWEEPS_AFTER_UNDECRYPTABLE = 1


class UndecryptableWatch(logging.Handler):
    """Conta os anúncios LDN que não decifraram, por canal."""

    MESSAGE = "Failed to parse advertisement frame"

    def __init__(self):
        super().__init__(logging.WARNING)
        self.channel = None
        self.by_channel = {}

    def emit(self, record):
        if self.MESSAGE in record.getMessage():
            key = self.channel if self.channel is not None else 0
            self.by_channel[key] = self.by_channel.get(key, 0) + 1

    @property
    def total(self):
        return sum(self.by_channel.values())


def frlg_matcher(engine_dir):
    """Função que diz se uma rede é a sala do FRLG, com as regras do motor (sem duplicar a lista)."""
    sys.path.insert(0, engine_dir)
    from frlgsim import transport

    ids = (transport.LiveTransport.LOCAL_COMMUNICATION_ID,) + tuple(
        getattr(transport.LiveTransport, "KNOWN_COMM_IDS", ()))
    looks = getattr(transport, "_looks_like_frlg_beacon", None)

    def is_frlg(network):
        if network.local_communication_id in ids:
            return True
        return bool(looks and looks(getattr(network, "application_data", b"") or b""))

    return is_frlg


def error_text(e, limit=300):
    """Mensagem legível: o trio embrulha as falhas em ExceptionGroup; mostra as causas de verdade."""
    leaves = []

    def walk(exc):
        subs = getattr(exc, "exceptions", None)
        if subs:
            for sub in subs:
                walk(sub)
        else:
            leaves.append(f"{type(exc).__name__}: {exc}")

    walk(e)
    return "; ".join(leaves)[:limit]


def describe(network, is_frlg):
    """Resumo de uma rede decifrada (nada de chaves nem de endereços)."""
    return {
        "commId": f"0x{network.local_communication_id:016x}",
        "channel": network.channel,
        "protocol": network.protocol,
        "frlg": is_frlg(network),
        "participants": network.num_participants,
        "maxParticipants": network.max_participants,
    }


def main(engine_dir):
    out = os.fdopen(os.dup(1), "w", buffering=1, encoding="utf-8")
    os.dup2(2, 1)
    sys.stdout = sys.stderr
    signal.signal(signal.SIGINT, signal.default_int_handler)  # o pai ignora SIGINT; o filho não
    params = json.loads(sys.stdin.readline())

    def emit(obj):
        out.write(json.dumps(obj) + "\n")
        out.flush()

    watch = UndecryptableWatch()
    logging.getLogger("ldn").addHandler(watch)

    try:
        is_frlg = frlg_matcher(engine_dir)
        import ldn
        import trio
        from ldn import wlan

        keys = ldn.load_keys(params["keys"])
    except Exception as e:  # noqa: BLE001
        emit({"type": "result", "result": {"passed": False, "code": "engine_error", "error": error_text(e)}})
        return 0

    result = run_scan(ldn, trio, wlan, keys, params, is_frlg, watch, emit)
    result.update(verdict(result))
    emit({"type": "result", "result": result})
    return 0


def run_scan(ldn, trio, wlan, keys, params, is_frlg, watch, emit):
    phy = params["phy"]
    seconds = float(params.get("seconds", DEFAULT_SECONDS))
    channels = [c for c in params.get("channels", CHANNELS) if wlan.is_valid_channel(c)]
    result = {"monitor": False, "channels": [], "networks": [], "undecryptable": 0,
              "undecryptableChannels": [], "sweeps": 0, "seconds": 0.0, "cancelled": False,
              "error": None}
    stats = {c: {"channel": c, "usable": True, "error": None} for c in channels}
    found = {}
    started = time.monotonic()

    async def scan():
        derivations = {p: ldn.KeyDerivation(keys, p) for p in (1, 3)}
        async with wlan.create_factory() as factory:
            async with factory.create_monitor(phy, TEST_VIF) as monitor:
                result["monitor"] = True
                emit({"type": "progress", "stage": "listening"})
                scanner = ldn.Scanner(derivations, monitor)

                async def receive():
                    while True:
                        network = await scanner.receive()
                        key = (network.address, network.local_communication_id)  # MACAddress é hashable
                        if key not in found:
                            found[key] = describe(network, is_frlg)
                            emit({"type": "progress", "stage": "network", "network": found[key]})

                deadline = started + seconds
                stop_after = None
                async with trio.open_nursery() as nursery:
                    nursery.start_soon(receive)
                    while time.monotonic() < deadline:
                        usable = [c for c in channels if stats[c]["usable"]]
                        if not usable:
                            break
                        for channel in usable:
                            try:
                                await monitor.set_channel(channel)
                            except Exception as e:  # noqa: BLE001 - placa sem 5 GHz, canal proibido
                                stats[channel]["usable"] = False
                                stats[channel]["error"] = error_text(e, 200)
                                continue
                            watch.channel = channel
                            await trio.sleep(DWELL)
                            if any(n["frlg"] for n in found.values()) or time.monotonic() >= deadline:
                                break
                        result["sweeps"] += 1
                        emit({"type": "progress", "stage": "sweep", "sweep": result["sweeps"],
                              "elapsed": round(time.monotonic() - started, 1)})
                        if any(n["frlg"] for n in found.values()):
                            break
                        if watch.total and stop_after is None:
                            stop_after = result["sweeps"] + EXTRA_SWEEPS_AFTER_UNDECRYPTABLE
                        if stop_after is not None and result["sweeps"] >= stop_after:
                            break
                    nursery.cancel_scope.cancel()

    try:
        trio.run(scan)
    except KeyboardInterrupt:
        result["cancelled"] = True
    except Exception as e:  # noqa: BLE001
        result["error"] = error_text(e)

    result["seconds"] = round(time.monotonic() - started, 1)
    result["channels"] = list(stats.values())
    result["networks"] = list(found.values())
    result["undecryptable"] = watch.total
    result["undecryptableChannels"] = sorted(c for c in watch.by_channel if c)
    return result


def verdict(result):
    """Resumo: `passed` e um código estável (o app traduz).

    Ordem: a sala do FRLG decifrada vence tudo. Se algum anúncio não decifrou, as chaves são o
    problema provável (mesmo que outra sessão tenha decifrado: a sala pode usar o outro protocolo
    LDN, e o prod.keys pode não ter a chave dele). Sessão de outro jogo decifrada prova as chaves,
    mas não a sala.
    """
    networks = result.get("networks", [])
    if any(n["frlg"] for n in networks):
        return {"passed": True, "code": "found_decrypted"}
    if result.get("cancelled"):
        return {"passed": False, "code": "cancelled"}
    if not result.get("monitor"):
        return {"passed": False, "code": "monitor_failed"}
    if result.get("channels") and not any(c["usable"] for c in result["channels"]):
        return {"passed": False, "code": "channel_failed"}
    if result.get("undecryptable"):
        return {"passed": False, "code": "found_not_decrypted"}
    if networks:
        return {"passed": False, "code": "other_session"}
    if result.get("error"):
        return {"passed": False, "code": "scan_failed"}
    return {"passed": False, "code": "not_found"}
