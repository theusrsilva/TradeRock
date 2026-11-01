"""ESP32 com o firmware do pokeldn, sem entrar em sala (roda como o usuário, sem pkexec; D23).

Entrada: uma linha JSON na entrada padrão. Saída: linhas JSON num descritor próprio; o `print` do pokeldn vai para a
saída de erro, com as sequências hexadecimais longas apagadas.

- {"op": "hello", "port": "COM3"} → {"type": "result", "target": "esp32s3", "version": "1.9.1"}
- {"op": "scan", "port": ..., "keys": "<caminho do prod.keys>", "channels": [1, 6, 11], "dwell": 1.0}
  → {"type": "result", "rooms": [...], "undecryptable": n}
Erros: {"type": "error", "code": "...", "message": "..."}.
"""

import json
import logging
import os
import re
import sys
import tempfile

from . import pokeldn_env

PORT_RE = re.compile(r"^(/dev/[A-Za-z0-9_.\-/]+|COM[0-9]{1,3})$")


class Emitter:
    def __init__(self, fd):
        self._out = os.fdopen(fd, "w", buffering=1, encoding="utf-8")

    def __call__(self, obj):
        self._out.write(json.dumps(obj, ensure_ascii=False) + "\n")
        self._out.flush()


class Undecryptable(logging.Handler):
    """A biblioteca LDN avisa quando vê um anúncio que não decifra com as chaves (D5)."""

    def __init__(self):
        super().__init__(logging.WARNING)
        self.count = 0

    def emit(self, record):
        if "Failed to parse advertisement frame" in record.getMessage():
            self.count += 1


def target_of(info_text):
    """"pokeldn-radio esp32s3 version=1.9.1 idf=..." → "esp32s3" (firmware/esp32/main/radio.c)."""
    parts = info_text.split()
    return parts[1] if len(parts) > 1 and parts[0] == "pokeldn-radio" else None


def hello(port):
    from pokeldn.ldn import esp32
    radio = esp32.Radio.open_serial(port)
    try:
        info = radio.info if getattr(radio, "info", None) else radio.hello()
        return {"target": target_of(info.text), "version": info.firmware_version}
    finally:
        radio.close()


def scan(port, keys_path, channels, dwell):
    os.environ["POKELDN_RADIO"] = f"esp32:{port}"
    import trio
    from pokeldn.ldn import api as ldn

    watch = Undecryptable()
    logging.getLogger("ldn").addHandler(watch)
    keys = ldn.load_keys(keys_path)
    networks = trio.run(lambda: ldn.scan(keys, phyname="esp32", channels=channels, dwell_time=dwell))
    rooms = [{
        "communicationId": n.local_communication_id,
        "channel": n.channel,
        "protocol": getattr(n, "protocol", 0),
        "members": n.num_participants,
        "maximum": n.max_participants,
        "policy": n.accept_policy,
    } for n in networks]
    return {"rooms": rooms, "undecryptable": watch.count}


def classify(error):
    """Erros de porta com os mesmos códigos da gravação (esp32_flash.classify); o resto é do firmware."""
    from .esp32_flash import classify as port_error
    code = port_error(str(error))
    if code.startswith("serial_"):
        return code
    if "protocol" in str(error).lower():
        return "esp32_firmware_other_version"
    if isinstance(error, (TimeoutError, OSError)) or type(error).__name__ == "RadioError":
        return "esp32_no_response"
    # Outro erro (do pokeldn ou da biblioteca LDN): a tela mostra a mensagem, sem dizer que a placa parou.
    return "esp32_error"


def main(root):
    emit = Emitter(os.dup(1))
    os.dup2(2, 1)
    sys.stdout = sys.stderr
    params = json.loads(sys.stdin.readline())
    port = params.get("port")
    if not isinstance(port, str) or not PORT_RE.match(port):
        emit({"type": "error", "code": "invalid", "message": "porta inválida"})
        return 2
    with tempfile.TemporaryDirectory(prefix="traderock-pokeldn-") as data:
        pokeldn_env.prepare(root, data)
        try:
            if params.get("op") == "hello":
                result = hello(port)
            elif params.get("op") == "scan":
                channels = [int(c) for c in params.get("channels", [1, 6, 11])]
                result = scan(port, params["keys"], channels, float(params.get("dwell", 1.0)))
            else:
                emit({"type": "error", "code": "invalid", "message": "op inválida"})
                return 2
        except Exception as e:  # noqa: BLE001 - vira um código para a tela
            sys.stderr.write(pokeldn_env.redact(f"{type(e).__name__}: {e}") + "\n")
            emit({"type": "error", "code": classify(e), "message": pokeldn_env.redact(str(e))})
            return 1
    emit({"type": "result", **result})
    return 0
