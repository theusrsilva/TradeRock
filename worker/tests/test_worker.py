"""Testes do worker (sem root, sem placa): validação, rádio, mapeamento de eventos, wrapper do
motor e o laço de protocolo. Rodam com `python3 -m unittest discover -s worker/tests -t worker`."""

import base64
import glob
import io
import json
import logging
import shutil
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

from traderock_worker import beacon_child, card_child, events, protocol, radio, server  # noqa: E402
from tests.fake_ldn import FakeTrio, make_ldn  # noqa: E402

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


class BeaconParamsTests(unittest.TestCase):
    def test_valido_e_padrao(self):
        p = protocol.beacon_params({"phy": "phy1", "keys": KEYS})
        self.assertEqual((p.phy, p.seconds), ("phy1", 30))
        self.assertEqual(protocol.beacon_params({"phy": "phy0", "keys": KEYS, "seconds": 60}).seconds, 60)

    def test_rejeita(self):
        for over in (dict(phy="phy0 x"), dict(keys=""), dict(keys="%%"), dict(seconds=1), dict(seconds=600),
                     dict(seconds="30")):
            msg = {"phy": "phy0", "keys": KEYS}
            msg.update(over)
            with self.subTest(over=over), self.assertRaises(protocol.RequestError):
                protocol.beacon_params(msg)


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

    def test_api_do_motor_usada_pelo_teste_do_beacon(self):
        """O beacon_child reconhece a sala do FRLG pelas regras do transport (submódulo + patch 0001)."""
        engine = os.path.join(REPO, "engines", "frlg-ldn-trade")
        if not os.path.isdir(os.path.join(engine, "frlgsim")):
            self.skipTest("submódulo ausente")
        with open(os.path.join(engine, "frlgsim", "transport.py"), encoding="utf-8") as f:
            transport = f.read()
        with open(os.path.join(REPO, "engines", "patches", "frlg-ldn-trade",
                               "0001-transport-scan-and-retry-fixes.patch"), encoding="utf-8") as f:
            patch = f.read()
        self.assertIn("class LiveTransport:", transport)
        self.assertIn("LOCAL_COMMUNICATION_ID = 0x0100610011000000", transport)
        self.assertIn("+    KNOWN_COMM_IDS = (", patch)
        self.assertIn("+def _looks_like_frlg_beacon(app_data):", patch)
        # canais e tempo por canal iguais aos da varredura do motor
        self.assertIn('"FRLG_CHANNELS", "1,6,11,36,40,44,48"', patch)
        self.assertIn('os.environ.get("FRLG_SCAN_DWELL", "0.60")', patch)
        self.assertEqual(beacon_child.CHANNELS, (1, 6, 11, 36, 40, 44, 48))
        self.assertEqual(beacon_child.DWELL, 0.6)

    def test_salvamento_sobrevive_a_evolucao_na_troca(self):
        """Patch 0003: a cadeia de salvamento pós-troca aguenta a pausa de uma evolução e, se a rede de
        segurança disparar, volta quando o host retoma as barreiras (antes, o Switch travava em "Saving...")."""
        engine = os.path.join(REPO, "engines", "frlg-ldn-trade")
        pk3 = os.path.join(REPO, "engines", "frlg-ldn-trade-esp32", "assets", "party", "mewtwo.pk3")
        if not (os.path.isdir(os.path.join(engine, "frlgsim")) and os.path.exists(pk3)):
            self.skipTest("submódulos ausentes")
        with tempfile.TemporaryDirectory() as tmp:
            shutil.copytree(os.path.join(engine, "frlgsim"), os.path.join(tmp, "frlgsim"))
            shutil.copy(os.path.join(engine, "frlgtrade.py"), tmp)
            for p in sorted(glob.glob(os.path.join(REPO, "engines", "patches", "frlg-ldn-trade", "*.patch"))):
                with open(p, "rb") as f:
                    subprocess.run(["patch", "--quiet", "-p0", "-d", tmp], stdin=f, check=True)
            script = r"""
import json, sys
from frlgsim import mon, rfu, trade
eng = trade.TradeEngine([mon.Mon.from_pk3(open(sys.argv[1], "rb").read())])
eng.barrier.local_count = 5
eng._save_barriers, eng.leaving = True, True      # logo depois do commit da última troca
quiet = {"positional": []}
slot = b"".join(w.to_bytes(2, "little") for w in rfu.exit_standby_words(5))
def run(frames, f):
    for _ in range(frames):
        eng.feed_in_frame(f)
        eng.tick()
out = {}
run(1200, quiet)                                 # ~20 s de evolução, sem barreira do host
out["evolucao"] = eng._save_barriers
run(3000, quiet)                                 # e mais (golpe novo): passa da rede de segurança
out["rede"] = eng._save_barriers
run(10, {"positional": [(0, slot)]})            # o host volta com a barreira count=5
run(100, quiet)
out["retomou"] = eng._save_barriers
out["proxima"] = [eng.barrier.mode, eng.barrier.initiated, eng.barrier.local_count]
print(json.dumps(out))
"""
            done = subprocess.run([sys.executable, "-c", script, pk3], cwd=tmp, capture_output=True,
                                  text=True, timeout=120)
        self.assertEqual(done.returncode, 0, done.stderr)
        out = json.loads(done.stdout)
        self.assertTrue(out["evolucao"])                 # 20 s quieto não encerra mais a cadeia
        self.assertFalse(out["rede"])                    # a rede de segurança ainda existe
        self.assertTrue(out["retomou"])                  # e o host de volta reabre a cadeia
        self.assertEqual(out["proxima"], ["STANDBY", True, 6])  # já iniciando a rodada seguinte


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

    def test_network_config_legivel_com_umask_do_worker(self):
        # O worker roda com umask 077; os arquivos precisam continuar legíveis para o app sem root.
        old = os.umask(0o077)
        try:
            with tempfile.TemporaryDirectory() as root:
                radio.NetworkConfig(root, FakeRunner()).install()
                for path in (radio.NM_CONF_PATH, radio.LINK_PATH):
                    mode = os.stat(os.path.join(root, path.lstrip("/"))).st_mode & 0o777
                    self.assertEqual(mode, 0o644, path)
        finally:
            os.umask(old)

    IW_SO_24GHZ = ("Wiphy phy0\n\tBand 1:\n\t\tFrequencies:\n"
                   "\t\t\t* 2412.0 MHz [1] (20.0 dBm)\n\t\t\t* 2437.0 MHz [6] (20.0 dBm)\n"
                   "\t\t\t* 2462.0 MHz [11] (20.0 dBm)\n\t\t\t* 2484.0 MHz [14] (20.0 dBm) (no IR)\n")
    IW_DUAS_BANDAS = IW_SO_24GHZ + ("\tBand 2:\n\t\tFrequencies:\n"
                                    "\t\t\t* 5180.0 MHz [36] (23.0 dBm)\n"
                                    "\t\t\t* 5200.0 MHz [40] (disabled)\n"
                                    "\t\t\t* 5220.0 MHz [44] (23.0 dBm) (no IR)\n")
    CANAIS = (1, 6, 11, 36, 40, 44, 48)

    def test_canais_placa_so_24ghz(self):
        # mt7601u: sem Band 2; a troca abortava no canal 36.
        runner = lambda cmd: (0, self.IW_SO_24GHZ)  # noqa: E731
        self.assertEqual(radio.supported_channels("phy0", self.CANAIS, runner), [1, 6, 11])

    def test_canais_duas_bandas_sem_os_desligados(self):
        calls = []

        def runner(cmd):
            calls.append(cmd)
            return 0, self.IW_DUAS_BANDAS

        self.assertEqual(radio.supported_channels("phy1", self.CANAIS, runner), [1, 6, 11, 36, 44])
        self.assertEqual(calls, [["iw", "phy", "phy1", "info"]])

    def test_canais_sem_iw_ou_sem_resposta_util(self):
        self.assertEqual(radio.supported_channels("phy0", self.CANAIS, lambda cmd: (127, "")),
                         list(self.CANAIS))
        self.assertEqual(radio.supported_channels("phy0", self.CANAIS, lambda cmd: (0, "Wiphy phy0\n")),
                         list(self.CANAIS))


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


class BeaconVerdictTests(unittest.TestCase):
    def test_ordem(self):
        v = beacon_child.verdict
        frlg = {"frlg": True}
        other = {"frlg": False}
        ok_ch = [{"channel": 1, "usable": True, "error": None}]
        base = {"monitor": True, "channels": ok_ch, "networks": [], "undecryptable": 0}
        self.assertEqual(v({**base, "networks": [frlg], "undecryptable": 3}), {"passed": True, "code": "found_decrypted"})
        self.assertEqual(v({**base, "cancelled": True})["code"], "cancelled")
        self.assertEqual(v({**base, "monitor": False})["code"], "monitor_failed")
        self.assertEqual(v({**base, "channels": [{"channel": 1, "usable": False}]})["code"], "channel_failed")
        self.assertEqual(v({**base, "networks": [other], "undecryptable": 1})["code"], "found_not_decrypted")
        self.assertEqual(v({**base, "networks": [other]})["code"], "other_session")
        self.assertEqual(v({**base, "error": "x"})["code"], "scan_failed")
        self.assertEqual(v(base), {"passed": False, "code": "not_found"})

    def test_erro_legivel(self):
        group = ExceptionGroup("Exceptions from Trio nursery", [OSError(95, "Operation not supported")])
        self.assertEqual(beacon_child.error_text(group), "OSError: [Errno 95] Operation not supported")


class BeaconScanTests(unittest.TestCase):
    """run_scan de verdade, com trio e ldn falsos e um "ar" roteirizado por canal."""

    def setUp(self):
        self._dwell = beacon_child.DWELL
        beacon_child.DWELL = 0.005
        self.watch = beacon_child.UndecryptableWatch()
        logging.getLogger("ldn").addHandler(self.watch)
        self.is_frlg = beacon_child.frlg_matcher(os.path.join(TESTS, "fake_engine"))

    def tearDown(self):
        beacon_child.DWELL = self._dwell
        logging.getLogger("ldn").removeHandler(self.watch)

    def scan(self, seconds=0.5, **fake):
        ldn, wlan, state = make_ldn(**fake)
        progress = []
        result = beacon_child.run_scan(ldn, FakeTrio, wlan, {}, {"phy": "phy0", "seconds": seconds},
                                       self.is_frlg, self.watch, progress.append)
        result.update(beacon_child.verdict(result))
        return result, state.get("monitor"), progress

    def test_sala_encontrada_para_na_hora(self):
        result, monitor, progress = self.scan(air={11: "frlg"})
        self.assertEqual(result["code"], "found_decrypted")
        self.assertEqual(monitor.visits, [1, 6, 11])  # não continua depois de achar
        self.assertEqual(result["networks"], [{"commId": "0x0100610011000000", "channel": 11, "protocol": 1,
                                               "frlg": True, "participants": 1, "maxParticipants": 2}])
        self.assertTrue(any(p.get("stage") == "network" for p in progress))

    def test_sala_pelo_formato_do_beacon(self):
        result, _, _ = self.scan(air={6: "shaped"})
        self.assertEqual(result["code"], "found_decrypted")

    def test_chaves_erradas_para_depois_de_uma_volta_extra(self):
        result, monitor, _ = self.scan(seconds=5, air={6: "bad"})
        self.assertEqual(result["code"], "found_not_decrypted")
        self.assertEqual(result["undecryptableChannels"], [6])
        self.assertEqual(result["sweeps"], 2)
        self.assertEqual(len(monitor.visits), 14)
        self.assertLess(result["seconds"], 5)

    def test_outro_jogo_nao_aprova(self):
        result, _, _ = self.scan(air={1: "other"})
        self.assertEqual(result["code"], "other_session")
        self.assertFalse(result["passed"])
        self.assertFalse(result["networks"][0]["frlg"])

    def test_nada_ate_o_tempo_acabar(self):
        result, _, _ = self.scan(seconds=0.2)
        self.assertEqual(result["code"], "not_found")
        self.assertGreaterEqual(result["sweeps"], 2)
        self.assertEqual(result["undecryptable"], 0)

    def test_placa_sem_5ghz_segue_nos_outros_canais(self):
        result, monitor, _ = self.scan(seconds=0.3, bad_channels=(36, 40, 44, 48), air={})
        self.assertEqual(result["code"], "not_found")
        unusable = [c["channel"] for c in result["channels"] if not c["usable"]]
        self.assertEqual(unusable, [36, 40, 44, 48])
        self.assertTrue(set(monitor.visits) <= {1, 6, 11})

    def test_nenhum_canal_e_monitor_falhou(self):
        result, _, _ = self.scan(bad_channels=(1, 6, 11, 36, 40, 44, 48))
        self.assertEqual(result["code"], "channel_failed")
        result, _, _ = self.scan(monitor_error="Operation not supported")
        self.assertEqual(result["code"], "monitor_failed")
        self.assertIn("Operation not supported", result["error"])


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
                if scenario == "no_network":
                    # A causa, que vem na linha de baixo do erro do motor, chega ao app.
                    self.assertEqual(out[-1]["message"], "LDN join failed after 1 attempt(s): "
                                     "no joinable FRLG network (saw 0, 0 joinable)")
                self.assertTrue(any(m["type"] == "debug" and m["text"].startswith("canais da varredura:")
                                    for m in out))

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
        if kind == "beacon":
            code = ("import json,sys; p=json.loads(sys.stdin.readline()); "
                    "k=open(p['keys'],'rb').read(); "
                    "print(json.dumps({'type':'progress','stage':'listening'})); "
                    "print(json.dumps({'type':'result','result':{'passed':True,'code':'found_decrypted',"
                    "'keysBytes':len(k),'seconds':p['seconds'],'keysPath':p['keys']}}))")
            return [sys.executable, "-c", code]
        if kind == "pokeldn":
            # O filho do pokeldn: confere o que o worker acrescentou ao pedido e responde no protocolo dele.
            code = ("import json,os,sys; p=json.loads(sys.stdin.readline()); "
                    "ok=os.path.isfile(p['keys']) and p['radio'] is None and p['phy']=='phy0' and p['game']=='sv'; "
                    "print(json.dumps({'type':'event','kind':'joined'})); "
                    "print(json.dumps({'type':'trade_done','index':1,'data':'eA=='})); "
                    "print(json.dumps({'type':'result','exitCode':0 if ok else 9,'trades':1}))")
            return [sys.executable, "-c", code]
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
        self.assertEqual(msg["result"]["protocol"], 2)
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

    def test_troca_pelo_pokeldn(self):
        """start_pokeldn (D23): as chaves vão num arquivo 600 do root, a placa é a escolhida, o rádio volta e cada
        mensagem do pokeldn chega ao app."""
        self.h.send({"id": 1, "op": "start_pokeldn", "phy": "phy0", "keys": KEYS,
                     "request": {"game": "sv", "offers": ["eA=="], "code": "12345678"}})
        started, _ = self.h.read_until(lambda m: m.get("id") == 1)
        self.assertTrue(started["ok"])
        end, seen = self.h.read_until(lambda m: m.get("event") == "pokeldn_end")
        self.assertEqual(end["exitCode"], 0)
        messages = [m["message"] for m in seen if m.get("event") == "pokeldn"]
        self.assertEqual([m["type"] for m in messages], ["event", "trade_done", "result"])
        self.assertEqual(messages[-1]["exitCode"], 0)  # o filho conferiu chaves, rádio e placa
        self.assertIn(["nmcli", "device", "set", "wlp2s0", "managed", "yes"], self.h.runner.calls)
        self.assertEqual(glob.glob(os.path.join(tempfile.gettempdir(), "traderock-pokeldn-*", "prod.keys")), [])

        self.h.send({"id": 2, "op": "start_pokeldn", "phy": "phy0", "keys": KEYS, "request": "x"})
        bad, _ = self.h.read_until(lambda m: m.get("id") == 2)
        self.assertEqual(bad["error"]["code"], "invalid_request")

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

    def test_beacon_test(self):
        self.h.send({"id": 1, "op": "beacon_test", "phy": "phy9", "keys": KEYS})
        msg, _ = self.h.read_until(lambda m: m.get("id") == 1)
        self.assertEqual(msg["error"]["code"], "phy_not_found")
        self.h.send({"id": 2, "op": "beacon_test", "phy": "phy0", "keys": KEYS, "seconds": 20})
        msg, seen = self.h.read_until(lambda m: m.get("id") == 2)
        result = msg["result"]
        self.assertEqual(result["code"], "found_decrypted")
        self.assertEqual(result["keysBytes"], len(base64.b64decode(KEYS)))
        self.assertEqual(result["seconds"], 20)
        self.assertTrue(any(m.get("event") == "debug" and "listening" in m["text"] for m in seen))
        # chaves apagadas e rádio devolvido
        self.assertFalse(os.path.exists(result["keysPath"]))
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
