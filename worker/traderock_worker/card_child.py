"""Processo filho (root): teste real da placa Wi-Fi.

O LDN exige duas coisas que o nome do driver não garante: colocar a placa em modo monitor e
transmitir quadros de ação (action frames) nesse modo. O teste faz exatamente isso com a própria
biblioteca LDN (a mesma que a troca usa), sem prod.keys e sem Switch:

1. cria a interface em modo monitor (`Factory.create_monitor`);
2. em cada canal (`Monitor.set_channel`), transmite quadros de ação (`Monitor.send_frame`);
3. escuta por um instante e conta os quadros recebidos (`Monitor.recv`): qualquer rede Wi-Fi por
   perto serve para mostrar que a recepção funciona.

Transmitir sem erro não prova que o quadro saiu no ar (isso exigiria um segundo rádio), mas é o que
separa os drivers que servem dos que recusam na hora.
"""

import json
import os
import signal
import sys

from .radio import TEST_VIF

# Quadro de ação "vendor specific" (categoria 0x7F) com um OUI localmente administrado e uma marca.
ACTION_BODY = b"\x7f" + b"\x02\x00\x00" + b"TRADEROCK-CARD-TEST"


def main():
    out = os.fdopen(os.dup(1), "w", buffering=1, encoding="utf-8")
    os.dup2(2, 1)
    sys.stdout = sys.stderr
    signal.signal(signal.SIGINT, signal.default_int_handler)  # o pai ignora SIGINT; o filho não
    params = json.loads(sys.stdin.readline())

    import trio
    from ldn import wlan

    phy = params["phy"]
    channels = params.get("channels", [1, 6, 11])
    listen = float(params.get("listenSeconds", 1.5))
    frames_per_channel = 5

    async def test():
        result = {"monitor": False, "channels": [], "error": None}
        try:
            async with wlan.create_factory() as factory:
                async with factory.create_monitor(phy, TEST_VIF) as monitor:
                    result["monitor"] = True
                    for channel in channels:
                        entry = {"channel": channel, "set": False, "sent": 0, "received": 0,
                                 "error": None}
                        result["channels"].append(entry)
                        try:
                            await monitor.set_channel(channel)
                            entry["set"] = True
                        except Exception as e:  # noqa: BLE001
                            entry["error"] = f"{type(e).__name__}: {e}"[:200]
                            continue
                        frame = wlan.ActionFrame()
                        frame.source = monitor.address()
                        frame.action = ACTION_BODY
                        try:
                            for _ in range(frames_per_channel):
                                await monitor.send_frame(frame)
                                entry["sent"] += 1
                                await trio.sleep(0.02)
                        except Exception as e:  # noqa: BLE001
                            entry["error"] = f"{type(e).__name__}: {e}"[:200]
                        with trio.move_on_after(listen):
                            while True:
                                await monitor.recv()
                                entry["received"] += 1
        except Exception as e:  # noqa: BLE001
            result["error"] = f"{type(e).__name__}: {e}"[:300]
        return result

    result = trio.run(test)
    result.update(verdict(result))
    out.write(json.dumps({"type": "result", "result": result}) + "\n")
    out.flush()
    return 0


def verdict(result):
    """Resumo: `passed` e um código estável (o app traduz)."""
    channels = result["channels"]
    if not result["monitor"]:
        return {"passed": False, "code": "monitor_failed"}
    if not any(c["set"] for c in channels):
        return {"passed": False, "code": "channel_failed"}
    usable = [c for c in channels if c["set"]]
    if any(c["sent"] == 0 for c in usable):
        return {"passed": False, "code": "tx_failed"}
    if len(usable) < len(channels):
        return {"passed": True, "code": "passed_some_channels"}
    if sum(c["received"] for c in usable) == 0:
        return {"passed": True, "code": "passed_no_rx"}
    return {"passed": True, "code": "passed"}
