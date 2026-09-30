"""Gravação do firmware do ESP32 com o esptool (roda como o usuário, sem root).

Usa a API pública do esptool 5 (`esptool.cmds`) em vez de ler o texto que ele imprime:
`detect_chip`, `run_stub`, `attach_flash`, `detect_flash_size`, `verify_flash`, `write_flash` e
`reset_chip`. O logger do esptool é trocado por um que emite o progresso em JSON.

Entrada: uma linha JSON na entrada padrão.
    {"op": "identify" | "verify" | "flash", "port": "/dev/ttyUSB0",
     "image": "/caminho/firmware.bin", "flashSize": "4MB", "flashMode": "dio", "flashFreq": "80m"}
Saída: linhas JSON.
    {"type": "progress", "stage": "connect|write|verify", "done": n, "total": m}
    {"type": "result", ...}   ou   {"type": "error", "code": "...", "message": "..."}
"""

import json
import os
import re
import sys

ROM_BAUD = 115200
FLASH_BAUD = 460800   # a mesma do firmware/release/README.md do easyworld
PORT_RE = re.compile(r"^(/dev/[A-Za-z0-9_.\-/]+|COM[0-9]{1,3})$")
SIZE_RE = re.compile(r"^(1|2|4|8|16|32|64|128)MB$")


class Emitter:
    def __init__(self, fd):
        self._out = os.fdopen(fd, "w", buffering=1, encoding="utf-8")

    def __call__(self, obj):
        self._out.write(json.dumps(obj, ensure_ascii=False) + "\n")
        self._out.flush()


def make_logger(emit, template_cls, stage):
    """Logger do esptool que manda o progresso ao app; o texto vai para a saída de erro (log)."""

    class JsonLogger(template_cls):
        def print(self, *args, **kwargs):
            text = " ".join(str(a) for a in args).strip()
            if text:
                sys.stderr.write(text + "\n")

        def note(self, message):
            sys.stderr.write(f"Note: {message}\n")

        def warning(self, message):
            sys.stderr.write(f"Warning: {message}\n")

        def error(self, message):
            sys.stderr.write(f"Error: {message}\n")

        def stage(self, finish=False):
            pass

        def progress_bar(self, cur_iter, total_iters, prefix="", suffix="", bar_length=30):
            emit({"type": "progress", "stage": stage["name"], "done": int(cur_iter), "total": int(total_iters)})

        def set_verbosity(self, verbosity):
            pass

    return JsonLogger()


def classify(text, windows=sys.platform == "win32"):
    """Mensagem de erro do esptool/pyserial → código estável (o app traduz)."""
    t = text.lower()
    # O esptool usa "could not open port" para todos; o errno do pyserial diz o motivo.
    if windows and "could not open port" in t:
        # Windows: "PermissionError(13, 'Acesso negado.', None, 5)" é porta em uso por outro programa
        # (não há grupo de permissão); FileNotFoundError(2, ...) é porta que não existe.
        if "permissionerror" in t or "(13," in t:
            return "serial_busy"
        return "serial_port_missing"
    if "permission denied" in t or "errno 13" in t:
        return "serial_permission"
    if "resource busy" in t or "errno 16" in t:
        return "serial_busy"
    if "could not open port" in t or "no such file or directory" in t:
        return "serial_port_missing"
    if "wrong boot mode" in t or "failed to connect" in t or "no serial data received" in t:
        return "boot_mode_required"
    if "verification failed" in t:
        return "verify_mismatch"
    return "esptool_error"


def validate(params):
    op = params.get("op")
    if op not in ("identify", "verify", "flash"):
        raise ValueError("op inválida")
    if not isinstance(params.get("port"), str) or not PORT_RE.match(params["port"]):
        raise ValueError("porta inválida")
    if op != "identify":
        if not isinstance(params.get("image"), str) or not os.path.isfile(params["image"]):
            raise ValueError("imagem ausente")
        if not SIZE_RE.match(str(params.get("flashSize", ""))):
            raise ValueError("flashSize inválido")
        if params.get("flashMode") not in ("dio", "qio", "dout", "qout"):
            raise ValueError("flashMode inválido")
        if params.get("flashFreq") not in ("80m", "40m", "26m", "20m"):
            raise ValueError("flashFreq inválido")
    return op


def target_of(chip_name):
    """'ESP32-S3' → 'esp32s3' (o mesmo nome que o firmware devolve no LDN_HELLO: CONFIG_IDF_TARGET)."""
    return chip_name.lower().replace("-", "")


def run(params, emit, esptool_cmds=None, esptool_logger=None):
    if esptool_cmds is None:
        from esptool import cmds as esptool_cmds  # noqa: PLC0415 - só no processo filho
        from esptool import logger as esptool_logger  # noqa: PLC0415
    op = validate(params)
    stage = {"name": "connect"}
    esptool_logger.log.set_logger(make_logger(emit, esptool_logger.TemplateLogger, stage))

    esp = esptool_cmds.detect_chip(params["port"], ROM_BAUD, "default-reset", False, 3)
    try:
        chip = esp.CHIP_NAME
        description = esp.get_chip_description()
        info = {
            "chip": chip,
            "target": target_of(chip),
            "description": description,
            "revision": esp.get_major_chip_version() * 100 + esp.get_minor_chip_version(),
            "features": list(esp.get_chip_features()),
        }
        esp = esptool_cmds.run_stub(esp)
        try:
            esp.change_baud(FLASH_BAUD)
        except Exception as e:  # noqa: BLE001 - alguns conversores não passam de 115200; segue devagar
            sys.stderr.write(f"change_baud: {e}\n")
        esptool_cmds.attach_flash(esp)
        info["flashSize"] = esptool_cmds.detect_flash_size(esp)

        if op == "identify":
            result = info
        else:
            addr = [(0, params["image"])]
            common = dict(flash_freq=params["flashFreq"], flash_mode=params["flashMode"], flash_size=params["flashSize"])
            if op == "flash":
                stage["name"] = "write"
                esptool_cmds.write_flash(esp, addr, compress=True, **common)
            stage["name"] = "verify"
            try:
                esptool_cmds.verify_flash(esp, addr, **common)
                matches = True
            except Exception as e:  # noqa: BLE001 - FatalError("Verification failed.")
                if "verification failed" not in str(e).lower():
                    raise
                matches = False
            result = dict(info, matches=matches, written=op == "flash")
    finally:
        # Volta a rodar o firmware (sai do modo de gravação) e fecha a porta.
        try:
            esptool_cmds.reset_chip(esp, "hard-reset")
        except Exception as e:  # noqa: BLE001
            sys.stderr.write(f"reset: {e}\n")
        try:
            esp._port.close()
        except Exception:  # noqa: BLE001
            pass
    emit({"type": "result", **result})


def main():
    emit = Emitter(os.dup(1))
    os.dup2(2, 1)  # qualquer print de biblioteca vai para o log
    sys.stdout = sys.stderr
    try:
        params = json.loads(sys.stdin.buffer.readline().decode("utf-8"))  # UTF-8 também no Windows
        run(params, emit)
    except ValueError as e:
        emit({"type": "error", "code": "invalid_request", "message": str(e)})
    except Exception as e:  # noqa: BLE001 - FatalError, SerialException etc. viram código
        text = f"{type(e).__name__}: {e}"
        emit({"type": "error", "code": classify(text), "message": text[:500]})
    return 0
