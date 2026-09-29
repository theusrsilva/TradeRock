"""ESP32 falso num pseudo-terminal: fala o protocolo serial do firmware do easyworld
(docs/SERIAL_PROTOCOL.md; host/core/SerialProtocol.cs) para testar o SerialDevice real sem placa.

Quadro: COBS(0x01, tipo, pedido u32, sessão u32, tamanho u16, dados, CRC32) terminado em 0.
Tipos: 1 comando do host, 2 linha de resposta ("LDN_DONE" fecha), 3 evento de texto.

Uso: python3 fake_esp32.py '<config json>'  → imprime o caminho do pty e atende até receber SIGTERM.
config: {"mode": "ok" | "silent" | "incapable", "target": "esp32s3",
         "adverts": [{"channel": 6, "mac": "02:00:00:00:00:01", "hex": "..."}]}
"""

import json
import os
import struct
import sys
import time
import tty
import zlib


def cobs_encode(data):
    out = bytearray([0])
    code_at, code = 0, 1
    for b in data:
        if b == 0:
            out[code_at] = code
            code_at = len(out)
            out.append(0)
            code = 1
        else:
            out.append(b)
            code += 1
            if code == 255:
                out[code_at] = code
                code_at = len(out)
                out.append(0)
                code = 1
    out[code_at] = code
    out.append(0)
    return bytes(out)


def cobs_decode(data):
    out, i = bytearray(), 0
    while i < len(data):
        code = data[i]
        i += 1
        out += data[i:i + code - 1]
        i += code - 1
        if code != 255 and i < len(data):
            out.append(0)
    return bytes(out)


def frame(kind, request, session, payload):
    body = struct.pack("<BBIIH", 1, kind, request, session, len(payload)) + payload
    return cobs_encode(body + struct.pack("<I", zlib.crc32(body)))


def main():
    config = json.loads(sys.argv[1])
    master, slave = os.openpty()
    tty.setraw(master)
    print(os.ttyname(slave), flush=True)
    session = 0
    buffer = bytearray()
    pending_adverts = []

    def send(kind, request, payload):
        os.write(master, frame(kind, request, session, payload.encode() if isinstance(payload, str) else payload))

    while True:
        for adv in pending_adverts:
            send(3, 0, f"LDN_ADV {adv['mac']} {adv['channel']} {adv['hex']}")
        pending_adverts.clear()
        try:
            chunk = os.read(master, 4096)
        except OSError:
            time.sleep(0.01)
            continue
        for b in chunk:
            if b != 0:
                buffer.append(b)
                continue
            raw, buffer = bytes(buffer), bytearray()
            if not raw:
                continue
            decoded = cobs_decode(raw)
            if len(decoded) < 16 or decoded[0] != 1:
                continue  # "\nLDN_BINARY\n" em texto puro, antes do modo binário
            _, kind, request, _sess, size = struct.unpack("<BBIIH", decoded[:12])
            command = decoded[12:12 + size].decode(errors="replace")
            if kind != 1 or config.get("mode") == "silent":
                continue
            name = command.split(" ")[0]
            if name == "LDN_HELLO":
                caps = "dynamic-session,scan" if config.get("mode") == "incapable" else "dynamic-session,scan,auth,udp"
                send(2, request, f"LDN_HELLO 1 {config.get('target', 'esp32s3')} {caps} 1472")
            elif name == "LDN_BEGIN":
                session = int(command.split(" ")[1], 16)
            elif name == "LDN_SCAN":
                channel = int(command.split(" ")[1])
                pending_adverts.extend(a for a in config.get("adverts", []) if a["channel"] == channel)
            elif name == "LDN_STOP":
                send(2, request, "LDN_DONE")
                session = 0
                continue
            send(2, request, "LDN_DONE")


if __name__ == "__main__":
    main()
