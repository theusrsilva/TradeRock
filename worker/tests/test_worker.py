"""Testes do worker (sem root, sem placa): validação, rádio, mapeamento de eventos, wrapper do
motor e o laço de protocolo. Rodam com `python3 -m unittest discover -s worker/tests -t worker`."""

import base64
import glob
import io
import json
import os
import subprocess
import sys
import tempfile
import threading
import time
import unittest

TESTS = os.path.dirname(os.path.abspath(__file__))
WORKER = os.path.dirname(TESTS)
REPO = os.path.dirname(WORKER)
sys.path.insert(0, WORKER)

from traderock_worker import card_child, events, protocol, radio, server  # noqa: E402

PK3 = base64.b64encode(bytes(100)).decode()
KEYS = base64.b64encode(b"master_key_00 = 00\n").decode()


def trade_msg(**over):
    msg = {"id": 1, "op": "start_trade", "phy": "phy0", "keys": KEYS, "party": [PK3, PK3],
           "slots": [1], "game": "leafgreen", "name": "ASH", "tid": 12345, "sid": None}
    msg.update(over)
    return msg


class ProtocolTests(unittest.TestCase):
    def test_valid(self):
        p = protocol.trade_params(trade_msg(sid=54321, approvalSeconds=90))
        self.assertEqual(p.phy, "phy0")
        self.assertEqual(len(p.party), 2)
        self.assertEqual(p.sid, 54321)
        self.assertEqual(p.approval_seconds, 90)

    def test_rejects(self):
        bad = [
            dict(phy="phy0; rm -rf /"), dict(phy="../phy0"), dict(party=[PK3]),
            dict(party=[PK3] * 7), dict(party=[PK3, "não é base64"]),
            dict(party=[PK3, base64.b64encode(bytes(50)).decode()]),
            dict(slots=[1, 1]), dict(slots=[2]), dict(slots=[]), dict(game="ruby"),
            dict(name=""), dict(name="NOMELONGO"), dict(name="A\nB"), dict(tid=70000),
            dict(tid="1"), dict(keys=""), dict(approvalSeconds=5),
        ]
        for over in bad:
            with self.subTest(over=over), self.assertRaises(protocol.RequestError):
                protocol.trade_params(trade_msg(**over))


class EventMapTests(unittest.TestCase):
    def test_classify(self):
        self.assertEqual(events.classify("Scanning for the FRLG network..."), "scanning")
        self.assertEqual(events.classify("Connection established."), "joined")
        self.assertEqual(events.classify("All 3 trade(s) complete; cancelling to leave."), "leaving")
        self.assertEqual(events.classify("algo novo"), "log")

    def test_frases_existem_no_motor(self):
        """Toda frase mapeada ainda existe no motor (submódulo + patches): o upstream mudou → falha aqui."""
        sources = ""
        for path in glob.glob(os.path.join(REPO, "engines", "frlg-ldn-trade", "**", "*.py"), recursive=True):
            with open(path, encoding="utf-8") as f:
                sources += f.read()
        for path in glob.glob(os.path.join(REPO, "engines", "patches", "frlg-ldn-trade", "*.patch")):
            with open(path, encoding="utf-8") as f:
                sources += f.read()
        if "Scanning for the FRLG network" not in sources:
            self.skipTest("submódulo engines/frlg-ldn-trade ausente")
        for prefix, _ in events.MILESTONES:
            with self.subTest(prefix=prefix):
                self.assertIn(prefix.strip(), sources)
        for needle, _, _ in events.JOIN_FAILURES:
            with self.subTest(needle=needle):
                self.assertIn(needle, sources)

    def test_api_do_motor_usada_pelo_wrapper(self):
        """Assinaturas do frlg-ldn-trade (commit do submódulo) em que o wrapper se apoia."""
        engine = os.path.join(REPO, "engines", "frlg-ldn-trade")
        if not os.path.isdir(os.path.join(engine, "frlgsim")):
            self.skipTest("submódulo ausente")

        def read(rel):
            with open(os.path.join(engine, rel), encoding="utf-8") as f:
                return f.read()

        frlgtrade, runtime, config = read("frlgtrade.py"), read("frlgsim/trade_runtime.py"), read("frlgsim/config.py")
        self.assertIn("def run_live(run_config, lg):", frlgtrade)
        self.assertIn("def save_received(engine, run_config, lg):", frlgtrade)
        self.assertIn("runtime.ConsoleLog(lg.verbose", frlgtrade)
        self.assertIn('def __init__(self, verbose, prefix="", *, start=None, output=print):', runtime)
        self.assertIn("def received_paths(mons, output_path, output_format, trades):", runtime)
        self.assertIn("def profile_from_overrides(*, ot=None, version=None, trainer_id=None,", config)
        for name in ("class TradePlan", "class LdnConfig", "class JoinerOptions", "class TradeRunConfig"):
            self.assertIn(name, config)


class FakeRunner:
    def __init__(self, nm_output="wlp2s0:connected\n"):
        self.calls = []
        self.nm_output = nm_output

    def __call__(self, cmd):
        self.calls.append(cmd)
        if cmd[:2] == ["nmcli", "-t"]:
            return (0, self.nm_output) if self.nm_output is not None else (127, "")
        return 0, ""


def fake_sysroot(root, ifaces):
    """/sys com phy0 e interfaces (nome → ligada?)."""
    os.makedirs(os.path.join(root, "sys", "class", "ieee80211", "phy0"))
    for name, up in ifaces.items():
        d = os.path.join(root, "sys", "class", "net", name)
        os.makedirs(d)
        os.symlink("../../ieee80211/phy0", os.path.join(d, "phy80211"))
        with open(os.path.join(d, "flags"), "w") as f:
            f.write("0x1003\n" if up else "0x1002\n")


class RadioTests(unittest.TestCase):
    def test_snapshot_take_restore(self):
        with tempfile.TemporaryDirectory() as root:
            fake_sysroot(root, {"wlp2s0": True, "ldnclient": True})
            runner = FakeRunner()
            r = radio.Radio(root, runner)
            snap = r.snapshot("phy0")
            self.assertEqual(snap, [radio.IfaceState("wlp2s0", True, True)])
            r.take(snap)
            self.assertIn(["ip", "link", "set", "wlp2s0", "down"], runner.calls)
            self.assertIn(["nmcli", "device", "set", "wlp2s0", "managed", "no"], runner.calls)
            runner.calls.clear()
            r.restore(snap)
            self.assertEqual(runner.calls, [
                ["iw", "dev", "ldnclient", "del"],
                ["ip", "link", "set", "wlp2s0", "up"],
                ["nmcli", "device", "set", "wlp2s0", "managed", "yes"],
            ])

    def test_sem_networkmanager_e_interface_desligada(self):
        with tempfile.TemporaryDirectory() as root:
            fake_sysroot(root, {"wlan0": False})
            runner = FakeRunner(nm_output=None)
            r = radio.Radio(root, runner)
            snap = r.snapshot("phy0")
            self.assertEqual(snap, [radio.IfaceState("wlan0", False, None)])
            runner.calls.clear()
            r.restore(snap)
            self.assertEqual(runner.calls, [])  # nada a religar

    def test_network_config(self):
        with tempfile.TemporaryDirectory() as root:
            runner = FakeRunner()
            nc = radio.NetworkConfig(root, runner)
            self.assertEqual(nc.status(), {"networkManager": False, "udevLink": False})
            self.assertEqual(nc.install(), {"networkManager": True, "udevLink": True})
            self.assertIn(["nmcli", "general", "reload", "conf"], runner.calls)
            mode = os.stat(os.path.join(root, radio.NM_CONF_PATH.lstrip("/"))).st_mode & 0o777
            self.assertEqual(mode & 0o022, 0)
            self.assertIn("interface-name:ldn*", radio.NM_CONF)


class BundledToolsTests(unittest.TestCase):
    def test_bin_embutido_vem_antes_do_sistema(self):
        path = server.child_env()["PATH"].split(":")
        self.assertEqual(path[0], radio.BUNDLED_BIN)
        self.assertEqual(os.path.basename(radio.BUNDLED_BIN), "bin")
        # <instalação>/worker/traderock_worker/radio.py → <instalação>/bin
        self.assertEqual(os.path.dirname(radio.BUNDLED_BIN), os.path.dirname(WORKER))

    def test_which_acha_o_embutido(self):
        with tempfile.TemporaryDirectory() as d:
            tool = os.path.join(d, "iw")
            with open(tool, "w") as f:
                f.write("#!/bin/sh\n")
            os.chmod(tool, 0o755)
            old = radio.SAFE_PATH
            try:
                radio.SAFE_PATH = d + ":/nao/existe"
                self.assertEqual(radio.which("iw"), tool)
            finally:
                radio.SAFE_PATH = old


class CardVerdictTests(unittest.TestCase):
    def ch(self, set_=True, sent=5, received=3):
        return {"channel": 1, "set": set_, "sent": sent, "received": received, "error": None}

    def test_verdicts(self):
        v = card_child.verdict
        self.assertEqual(v({"monitor": False, "channels": []})["code"], "monitor_failed")
        self.assertEqual(v({"monitor": True, "channels": [self.ch(set_=False)]})["code"], "channel_failed")
        self.assertEqual(v({"monitor": True, "channels": [self.ch(sent=0)]}), {"passed": False, "code": "tx_failed"})
        self.assertEqual(v({"monitor": True, "channels": [self.ch(), self.ch(set_=False)]})["code"], "passed_some_channels")
        self.assertEqual(v({"monitor": True, "channels": [self.ch(received=0)]})["code"], "passed_no_rx")
        self.assertEqual(v({"monitor": True, "channels": [self.ch()]}), {"passed": True, "code": "passed"})


def run_trade_child(params, scenario, timeout=20):
    """Roda o trade_child de verdade contra o motor falso (tests/fake_engine)."""
    env = {"PATH": os.environ.get("PATH", ""), "FAKE_SCENARIO": scenario}
    code = ("import sys; sys.path.insert(0, %r); from traderock_worker import trade_child; "
            "sys.exit(trade_child.main(%r))") % (WORKER, os.path.join(TESTS, "fake_engine"))
    done = subprocess.run([sys.executable, "-c", code], input=json.dumps(params) + "\n",
                          capture_output=True, text=True, timeout=timeout, env=env)
    return [json.loads(line) for line in done.stdout.splitlines()], done.stderr


class TradeChildTests(unittest.TestCase):
    def params(self, tmp, trades=1, name="ASH"):
        party = []
        for i in range(3):
            p = os.path.join(tmp, f"p{i}.pk3")
            with open(p, "wb") as f:
                f.write(bytes(100))
            party.append(p)
        return {"phy": "phy0", "keys": os.path.join(tmp, "k"), "party": party,
                "slots": list(range(trades)), "game": "firered", "name": name, "tid": 1, "sid": None,
                "out": os.path.join(tmp, "received.pk3")}

    def test_troca_completa_emite_marcos_e_recebidos(self):
        with tempfile.TemporaryDirectory() as tmp:
            out, _ = run_trade_child(self.params(tmp, trades=2), "success")
        kinds = [m.get("kind") for m in out if m["type"] == "event"]
        for k in ("scanning", "beacon_decrypted", "join_requested", "joined", "in_room", "host_sat",
                  "cards_exchanged", "trade_menu_open", "trade_offered", "confirming", "leaving", "link_closed"):
            self.assertIn(k, kinds)
        self.assertLess(kinds.index("beacon_decrypted"), kinds.index("join_requested"))
        offered = [m["tradeIndex"] for m in out if m.get("kind") == "trade_offered"]
        self.assertEqual(offered, [0, 1])
        received = [m for m in out if m["type"] == "received"]
        self.assertEqual([r["tradeIndex"] for r in received], [0, 1])  # um por troca, sem repetir
        self.assertEqual(base64.b64decode(received[1]["data"]), bytes([1]) * 100)
        self.assertTrue(any(m["type"] == "debug" and "verbose line" in m["text"] for m in out))
        self.assertEqual(out[-1], {"type": "end", "outcome": "completed", "code": None, "message": None,
                                   "retryable": False})

    def test_falhas(self):
        cases = {
            "no_network": ("no_network_found", True),
            "bad_keys": ("beacon_not_decrypted", False),
            "host_cancels": ("host_cancelled", False),
        }
        for scenario, (code, retryable) in cases.items():
            with self.subTest(scenario=scenario), tempfile.TemporaryDirectory() as tmp:
                out, _ = run_trade_child(self.params(tmp), scenario)
                self.assertEqual(out[-1]["outcome"], "failed")
                self.assertEqual(out[-1]["code"], code)
                self.assertEqual(out[-1]["retryable"], retryable)

    def test_nome_que_o_jogo_nao_aceita(self):
        with tempfile.TemporaryDirectory() as tmp:
            out, _ = run_trade_child(self.params(tmp, name="A@B"), "success")
        self.assertEqual(out[-1]["code"], "invalid_request")


class ServerHarness:
    """Servidor real com rádio falso e filhos falsos, falando por pipes como o app fala."""

    def __init__(self, child_cmd=None):
        self.tmp = tempfile.TemporaryDirectory()
        fake_sysroot(self.tmp.name, {"wlp2s0": True})
        self.runner = FakeRunner()
        rin, self.win = os.pipe()
        self.rout, wout = os.pipe()
        self.inp = os.fdopen(rin, "rb")
        self.writer = os.fdopen(self.win, "wb", buffering=0)
        self.reader = os.fdopen(self.rout, "r", encoding="utf-8")
        out = os.fdopen(wout, "w", encoding="utf-8")
        self.server = server.Server(out, radio=radio.Radio(self.tmp.name, self.runner),
                                    netconf=radio.NetworkConfig(self.tmp.name, self.runner),
                                    child_cmd=child_cmd or self.fake_child, require_root=False,
                                    which_tool=lambda t: None if t in self.missing else f"/usr/bin/{t}")
        self.missing = set()
        self.thread = threading.Thread(target=self.server.serve, args=(self.inp,), daemon=True)
        self.thread.start()
        self.scenario = "success"

    def fake_child(self, kind):
        if kind == "card":
            code = ("import json,sys; sys.stdin.readline(); print(json.dumps({'type':'result','result':"
                    "{'monitor':True,'channels':[],'passed':True,'code':'passed'}}))")
            return [sys.executable, "-c", code]
        code = ("import sys, os; os.environ['FAKE_SCENARIO']=%r; sys.path.insert(0, %r); "
                "from traderock_worker import trade_child; sys.exit(trade_child.main(%r))") % (
            self.scenario, WORKER, os.path.join(TESTS, "fake_engine"))
        return [sys.executable, "-c", code]

    def send(self, obj):
        self.writer.write((json.dumps(obj) + "\n").encode())

    def read_until(self, pred, timeout=20):
        seen = []
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            line = self.reader.readline()
            if not line:
                break
            msg = json.loads(line)
            seen.append(msg)
            if pred(msg):
                return msg, seen
        raise AssertionError(f"não chegou; visto: {seen[-5:]}")

    def close(self):
        self.writer.close()
        self.thread.join(60)
        self.reader.close()
        self.tmp.cleanup()


class ServerTests(unittest.TestCase):
    def setUp(self):
        self.h = ServerHarness()

    def tearDown(self):
        self.h.close()

    def test_hello_e_operacao_desconhecida(self):
        self.h.send({"id": 1, "op": "hello"})
        msg, _ = self.h.read_until(lambda m: m.get("id") == 1)
        self.assertEqual(msg["result"]["protocol"], 1)
        self.h.send({"id": 2, "op": "rm"})
        msg, _ = self.h.read_until(lambda m: m.get("id") == 2)
        self.assertEqual(msg["error"]["code"], "unknown_op")
        self.h.writer.write(b"{nao json\n")
        msg, _ = self.h.read_until(lambda m: "error" in m and m["id"] is None)
        self.assertEqual(msg["error"]["code"], "invalid_request")

    def test_troca_de_ponta_a_ponta(self):
        self.h.send(trade_msg(slots=[0, 1]))
        started, _ = self.h.read_until(lambda m: m.get("id") == 1)
        self.assertTrue(started["ok"])
        end, seen = self.h.read_until(lambda m: m.get("event") == "trade_end")
        self.assertEqual(end["outcome"], "completed")
        self.assertEqual(end["received"], 2)
        done = [m for m in seen if m.get("kind") == "trade_done"]
        self.assertEqual([d["tradeIndex"] for d in done], [0, 1])
        # o rádio voltou ao estado anterior
        self.assertIn(["ip", "link", "set", "wlp2s0", "up"], self.h.runner.calls)
        self.assertIn(["nmcli", "device", "set", "wlp2s0", "managed", "yes"], self.h.runner.calls)
        # pasta temporária com as chaves foi apagada
        self.assertEqual(glob.glob(os.path.join(tempfile.gettempdir(), "traderock-trade-*", "prod.keys")), [])

    def test_ocupado_e_cancelar(self):
        self.h.scenario = "hang"
        self.h.send(trade_msg())
        self.h.read_until(lambda m: m.get("kind") == "join_requested")
        self.h.send(trade_msg(id=2))
        busy, _ = self.h.read_until(lambda m: m.get("id") == 2)
        self.assertEqual(busy["error"]["code"], "busy")
        self.h.send({"id": 3, "op": "cancel"})
        end, _ = self.h.read_until(lambda m: m.get("event") == "trade_end")
        self.assertEqual(end["outcome"], "cancelled")
        self.h.send({"id": 4, "op": "status"})
        status, _ = self.h.read_until(lambda m: m.get("id") == 4)
        self.assertIsNone(status["result"]["busy"])

    def test_phy_inexistente_e_card_test(self):
        self.h.send({"id": 1, "op": "card_test", "phy": "phy9"})
        msg, _ = self.h.read_until(lambda m: m.get("id") == 1)
        self.assertEqual(msg["error"]["code"], "phy_not_found")
        self.h.send({"id": 2, "op": "card_test", "phy": "phy0"})
        msg, _ = self.h.read_until(lambda m: m.get("id") == 2)
        self.assertTrue(msg["result"]["passed"])
        self.assertIn(["ip", "link", "set", "wlp2s0", "down"], self.h.runner.calls)
        self.assertIn(["ip", "link", "set", "wlp2s0", "up"], self.h.runner.calls)

    def test_ferramentas_faltando(self):
        self.h.missing = {"iw"}
        self.h.send({"id": 1, "op": "hello"})
        msg, _ = self.h.read_until(lambda m: m.get("id") == 1)
        self.assertEqual(msg["result"]["missingTools"], ["iw"])
        self.h.send(trade_msg(id=2))
        msg, _ = self.h.read_until(lambda m: m.get("id") == 2)
        self.assertEqual(msg["error"], {"code": "missing_tools", "message": "iw"})

    def test_rede(self):
        self.h.send({"id": 1, "op": "net_install"})
        msg, _ = self.h.read_until(lambda m: m.get("id") == 1)
        self.assertEqual(msg["result"], {"networkManager": True, "udevLink": True})


class ReceivedFilesTests(unittest.TestCase):
    def test_nomes(self):
        with tempfile.TemporaryDirectory() as d:
            for n in ("received.pk3", "received_trade2_PIKACHU.pk3", "outro.pk3"):
                open(os.path.join(d, n), "wb").close()
            self.assertEqual([i for i, _ in server.received_files(d)], [0, 1])


if __name__ == "__main__":
    unittest.main()
