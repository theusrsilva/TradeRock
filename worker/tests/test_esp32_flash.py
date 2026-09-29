"""Gravação do ESP32 (esp32_flash.py) contra um esptool falso com a mesma API (esptool.cmds/logger 5.4)."""

import os
import sys
import tempfile
import types
import unittest

WORKER = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, WORKER)

from traderock_worker import esp32_flash  # noqa: E402


class FakeLog:
    def set_logger(self, logger):
        self.logger = logger


class FakeEsp:
    CHIP_NAME = "ESP32-S3"

    def __init__(self, fake):
        self.fake = fake
        self._port = types.SimpleNamespace(close=lambda: fake.calls.append("close"))

    def get_chip_description(self):
        return "ESP32-S3 (QFN56) (revision v0.2)"

    def get_major_chip_version(self):
        return 0

    def get_minor_chip_version(self):
        return 2

    def get_chip_features(self):
        return ["Wi-Fi", "BT 5 (LE)"]

    def change_baud(self, baud):
        self.fake.calls.append(("baud", baud))


class FakeCmds:
    """Imita esptool.cmds: registra as chamadas e emite progresso pelo logger instalado."""

    def __init__(self, log, flash_matches=True, connect_error=None):
        self.log = log
        self.calls = []
        self.flash_matches = flash_matches
        self.connect_error = connect_error

    def detect_chip(self, port, baud, connect_mode, trace, attempts):
        self.calls.append(("detect", port, baud, connect_mode))
        if self.connect_error:
            raise RuntimeError(self.connect_error)
        return FakeEsp(self)

    def run_stub(self, esp):
        self.calls.append("stub")
        return esp

    def attach_flash(self, esp):
        self.calls.append("attach")

    def detect_flash_size(self, esp):
        return "16MB"

    def write_flash(self, esp, addr, **kw):
        self.calls.append(("write", addr, kw))
        for i in range(3):
            self.log.logger.progress_bar(i + 1, 3)

    def verify_flash(self, esp, addr, **kw):
        self.calls.append(("verify", addr, kw))
        if not self.flash_matches:
            raise RuntimeError("Verification failed.")

    def reset_chip(self, esp, mode):
        self.calls.append(("reset", mode))


class Esp32FlashTests(unittest.TestCase):
    def setUp(self):
        self.log = FakeLog()
        self.logger_module = types.SimpleNamespace(log=self.log, TemplateLogger=object)
        self.out = []
        self.image = tempfile.NamedTemporaryFile(suffix=".bin", delete=False)
        self.image.write(b"\xe9" + bytes(100))
        self.image.close()

    def tearDown(self):
        os.unlink(self.image.name)

    def run_op(self, cmds, **params):
        base = {"port": "/dev/ttyUSB0", "image": self.image.name, "flashSize": "16MB", "flashMode": "dio", "flashFreq": "80m"}
        base.update(params)
        esp32_flash.run(base, self.out.append, cmds, self.logger_module)
        return self.out[-1]

    def test_identificar(self):
        cmds = FakeCmds(self.log)
        result = self.run_op(cmds, op="identify")
        self.assertEqual(result["target"], "esp32s3")
        self.assertEqual(result["revision"], 2)
        self.assertEqual(result["flashSize"], "16MB")
        self.assertIn(("reset", "hard-reset"), cmds.calls)  # sai do modo de gravação
        self.assertIn("close", cmds.calls)
        self.assertNotIn("write", [c[0] for c in cmds.calls if isinstance(c, tuple)])

    def test_gravar_com_progresso_e_conferencia(self):
        cmds = FakeCmds(self.log)
        result = self.run_op(cmds, op="flash")
        self.assertTrue(result["matches"])
        self.assertTrue(result["written"])
        write = next(c for c in cmds.calls if isinstance(c, tuple) and c[0] == "write")
        self.assertEqual(write[1], [(0, self.image.name)])  # tudo em 0x0, como o README do easyworld
        self.assertEqual(write[2], {"compress": True, "flash_freq": "80m", "flash_mode": "dio", "flash_size": "16MB"})
        progress = [o for o in self.out if o["type"] == "progress"]
        self.assertEqual([(p["stage"], p["done"]) for p in progress], [("write", 1), ("write", 2), ("write", 3)])
        self.assertIn(("baud", 460800), cmds.calls)

    def test_conferir_diferente(self):
        result = self.run_op(FakeCmds(self.log, flash_matches=False), op="verify")
        self.assertFalse(result["matches"])
        self.assertFalse(result["written"])

    def test_validacao(self):
        for bad in ({"op": "erase"}, {"op": "flash", "port": "/dev/tty; rm"}, {"op": "flash", "flashSize": "3MB"},
                    {"op": "flash", "image": "/nao/existe.bin"}):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                self.run_op(FakeCmds(self.log), **bad)

    def test_codigos_de_erro(self):
        cases = {
            "SerialException: [Errno 13] could not open port /dev/ttyUSB0: [Errno 13] Permission denied": "serial_permission",
            "SerialException: [Errno 2] could not open port /dev/ttyUSB9: [Errno 2] No such file or directory": "serial_port_missing",
            "FatalError: Could not open /dev/ttyUSB0, the port is busy or doesn't exist.\n([Errno 16] could not open port /dev/ttyUSB0: [Errno 16] Device or resource busy: '/dev/ttyUSB0')": "serial_busy",
            "FatalError: Failed to connect to ESP32-S3: Wrong boot mode detected (0x13)!": "boot_mode_required",
            "FatalError: Failed to connect to Espressif device: No serial data received.": "boot_mode_required",
            "FatalError: algo novo": "esptool_error",
        }
        for text, code in cases.items():
            with self.subTest(text=text):
                self.assertEqual(esp32_flash.classify(text), code)

    def test_esptool_real_tem_a_api_usada(self):
        """Confere, no esptool instalado (Python embutido), os nomes da API que o script usa."""
        try:
            from esptool import cmds, logger  # noqa: PLC0415
        except ImportError:
            self.skipTest("esptool não instalado neste Python")
        for name in ("detect_chip", "run_stub", "attach_flash", "detect_flash_size", "write_flash", "verify_flash", "reset_chip"):
            self.assertTrue(hasattr(cmds, name), name)
        self.assertTrue(hasattr(logger, "TemplateLogger"))
        self.assertTrue(hasattr(logger.log, "set_logger"))


if __name__ == "__main__":
    unittest.main()
