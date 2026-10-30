"""Testes da integração com o pokeldn (D21): patches do modo TradeRock e trava de rede."""

import io
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest

TESTS = os.path.dirname(os.path.abspath(__file__))
WORKER = os.path.dirname(TESTS)
REPO = os.path.dirname(WORKER)


def run_python(code, *, cwd=None, env=None, python=None):
    full_env = {k: v for k, v in os.environ.items() if not k.startswith(("POKELDN_", "TRADEROCK"))}
    full_env.update(env or {})
    return subprocess.run([python or sys.executable, "-c", textwrap.dedent(code)], cwd=cwd, env=full_env,
                          capture_output=True, text=True, timeout=60)


class PokeldnPatchTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not os.path.isfile(os.path.join(REPO, "engines", "pokeldn", "pokeldn", "__init__.py")):
            raise unittest.SkipTest("submódulo engines/pokeldn ausente")
        cls.tmp = tempfile.TemporaryDirectory()
        cls.engine = os.path.join(cls.tmp.name, "engine")
        subprocess.run([os.path.join(REPO, "scripts", "stage-pokeldn.sh"), cls.engine], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def run_engine(self, code, traderock, python=None):
        env = {"POKELDN_DATA": os.path.join(self.tmp.name, "data")}
        if traderock:
            env["TRADEROCK"] = "1"
        return run_python("import sys; sys.path.insert(0, '.')\n" + textwrap.dedent(code), cwd=self.engine, env=env,
                          python=python)

    def test_copia_tem_o_que_o_worker_usa(self):
        for rel in ("pokeldn/__init__.py", "bin/sv_join.py", "vendor/LDN/ldn/__init__.py", "LICENSE",
                    "vendor/LDN/LICENSE", "firmware/esp32/version.txt", "PATCHES.md"):
            with self.subTest(rel=rel):
                self.assertTrue(os.path.isfile(os.path.join(self.engine, rel)))
        self.assertFalse(os.path.exists(os.path.join(self.engine, "gui")))

    def test_sem_internet(self):
        r = self.run_engine("""
            from pokeldn.app import update
            try:
                update._open("https://api.github.com/")
            except OSError as e:
                print("recusado:", e)
            """, traderock=True)
        self.assertIn("recusado: TradeRock", r.stdout, r.stderr)

    def test_sem_plugins(self):
        r = self.run_engine("""
            import os
            os.environ["POKELDN_PLUGINS"] = "qualquer"
            from pokeldn import plugins
            print(plugins.load())
            """, traderock=True)
        self.assertEqual(r.stdout.strip(), "[]", r.stderr)

    def test_oferta_sem_pkhex_do_pokeldn(self):
        code = """
            from pokeldn import pokemon
            try:
                print(len(pokemon.prepare_offer("swsh", bytes(344))))
            except pokemon.BuilderError as e:
                print("bloqueado:", e)
            """
        self.assertEqual(self.run_engine(code, traderock=True).stdout.strip(), "344")
        # Sem TRADEROCK=1 o pokeldn segue o upstream: exige o ajudante PKHeX dele.
        self.assertIn("bloqueado:", self.run_engine(code, traderock=False).stdout)

    def test_presente_do_swsh_sem_pkhex_do_pokeldn(self):
        # Com TRADEROCK=1 o cartão com tamanho e soma certos passa sem o ajudante; um cartão estragado, não.
        code = """
            try:
                from pokeldn import pokemon
                from pokeldn.swsh import wc8
            except ModuleNotFoundError as e:
                raise SystemExit(f"sem-deps {e.name}")
            for data in (bytes(wc8.seal(bytearray(0x2D0))), bytes(10)):
                try:
                    pokemon.SERVICE.validate_gift(data)
                    print("ok")
                except pokemon.BuilderError:
                    print("bloqueado")
            """
        r = self.run_engine(code, traderock=True, python=pokeldn_python())
        if "sem-deps" in r.stderr:
            self.skipTest(f"o Python dos testes não tem as dependências do pokeldn ({r.stderr.strip()})")
        self.assertEqual(r.stdout.split(), ["ok", "bloqueado"], r.stderr)

    def test_oferta_do_pla_sem_pkhex_do_pokeldn(self):
        # O pla_host chama pokemon.validate direto, fora do prepare_offer.
        code = """
            from pokeldn import pokemon
            try:
                print(len(pokemon.validate("pla", bytes(376))))
            except pokemon.BuilderError as e:
                print("bloqueado:", e)
            """
        self.assertEqual(self.run_engine(code, traderock=True).stdout.strip(), "376")
        self.assertIn("bloqueado:", self.run_engine(code, traderock=False).stdout)


class NetguardTests(unittest.TestCase):
    def guarded(self, code):
        return run_python(textwrap.dedent(f"""
            import sys
            sys.path.insert(0, {WORKER!r})
            from traderock_worker import netguard
            netguard.install()
            import socket
            def tenta(f):
                try:
                    f()
                    print("ok")
                except netguard.BlockedNetwork:
                    print("bloqueado")
                except OSError:
                    print("ok")  # passou pela trava; a rede em si pode falhar no teste
            """) + textwrap.dedent(code))

    def test_internet_bloqueada(self):
        r = self.guarded("""
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(0.2)
            tenta(lambda: s.connect(("8.8.8.8", 443)))
            u = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            tenta(lambda: u.sendto(b"x", ("1.1.1.1", 53)))
            tenta(lambda: u.sendmsg([b"x"], [], 0, ("1.1.1.1", 53)))
            tenta(lambda: socket.getaddrinfo("example.com", 443))
            tenta(lambda: socket.gethostbyname("example.com"))
            """)
        self.assertEqual(r.stdout.split(), ["bloqueado"] * 5, r.stderr)

    def test_rede_local_liberada(self):
        r = self.guarded("""
            u = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            tenta(lambda: u.sendto(b"x", ("127.0.0.1", 9)))
            tenta(lambda: u.sendto(b"x", ("192.168.0.255", 9)))
            tenta(lambda: u.sendto(b"x", ("169.254.1.1", 9)))
            tenta(lambda: u.sendto(b"x", ("10.0.0.2", 9)))
            tenta(lambda: socket.getaddrinfo("192.168.0.10", 30000))
            tenta(lambda: socket.getaddrinfo(None, 30000))
            """)
        self.assertEqual(r.stdout.split(), ["ok"] * 6, r.stderr)

    def test_netlink_e_unix_passam(self):
        r = self.guarded("""
            a, b = socket.socketpair()
            tenta(lambda: a.send(b"x"))
            if hasattr(socket, "AF_NETLINK"):
                n = socket.socket(socket.AF_NETLINK, socket.SOCK_RAW, 0)
                tenta(lambda: n.sendto(b"", (0, 0)))
            else:
                print("ok")
            """)
        self.assertEqual(r.stdout.split(), ["ok", "ok"], r.stderr)




def pokeldn_python():
    """Python com as dependências do pokeldn: o do build do worker ou o do CI (TRADEROCK_POKELDN_PYTHON)."""
    for path in (os.environ.get("TRADEROCK_POKELDN_PYTHON"), os.path.join(REPO, "build", "worker", "stage", "python", "bin", "python3")):
        if path and os.path.isfile(path):
            return path
    return None


class PokeldnTradeTests(unittest.TestCase):
    """O processo de troca do pokeldn (pokeldn_trade.py, D23)."""

    def setUp(self):
        sys.path.insert(0, WORKER)
        from traderock_worker import pokeldn_trade
        self.trade = pokeldn_trade
        self.tmp = tempfile.TemporaryDirectory()
        self.keys = os.path.join(self.tmp.name, "prod.keys")
        open(self.keys, "w").close()

    def tearDown(self):
        self.tmp.cleanup()

    def params(self, **over):
        p = {"game": "sv", "offers": ["eA=="], "code": "12345678", "keys": self.keys, "radio": "esp32:COM3",
             "trainer": {"name": "Ana", "tid": 123456, "sid": 1234}}
        p.update(over)
        return p

    def test_validacao(self):
        self.assertEqual(self.trade.validate(self.params()), "sv")
        self.assertEqual(self.trade.validate(self.params(code="0,4,9", game="lgpe", radio=None)), "lgpe")
        for bad in (dict(game="xy"), dict(offers=[]), dict(offers=["eA=="] * 7), dict(code="123"),
                    dict(radio="esp32:/tmp/x;rm"), dict(radio="wifi"), dict(keys="/nao/existe"),
                    dict(game="frlg", trades=3, version="firered"), dict(game="frlg", trades=1, version="emerald")):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                self.trade.validate(self.params(**bad))
        self.assertEqual(self.trade.validate(self.params(game="frlg", trades=1, version="leafgreen", code=None)), "frlg")
        # Modo LAN (D24): só o Sword/Shield, sem prod.keys.
        self.assertEqual(self.trade.validate(self.params(game="swsh", radio="lan", keys=None)), "swsh")
        with self.assertRaises(ValueError):
            self.trade.validate(self.params(game="sv", radio="lan", keys=None))

    def test_validacao_do_mystery_gift(self):
        gift = dict(activity="gift", offers=None, code=None)
        ok = [dict(game="frlg", version="firered", gift={"mode": "preset", "preset": "celebi"}),
              dict(game="swsh", gift={"mode": "event", "event": "pokemon-go-transfer-gift-melmetal"}),
              dict(game="swsh", gift={"mode": "file", "extension": "wc8", "data": "eA=="}),
              dict(game="frlg", version="leafgreen", gift={"mode": "file", "extension": "wc3", "data": "eA=="})]
        for good in ok:
            with self.subTest(good=good):
                self.assertEqual(self.trade.validate(self.params(**gift, **good)), good["game"])
        bad = [dict(game="sv", gift={"mode": "preset", "preset": "x"}),            # sem Mystery Gift
               dict(game="frlg", gift={"mode": "preset", "preset": "celebi"}),     # sem versão
               dict(game="frlg", version="firered", gift={"mode": "event", "event": "x"}),  # FRLG sem eventos
               dict(game="swsh", gift={"mode": "file", "extension": "wc3", "data": "eA=="}),
               dict(game="swsh", gift={"mode": "file", "extension": "wc8", "data": ""}),
               dict(game="swsh", gift={"mode": "preset", "preset": "../x"}),
               dict(game="swsh", gift={"mode": "save"}),
               dict(game="swsh", radio="lan", keys=None, gift={"mode": "preset", "preset": "pikachu"})]
        for wrong in bad:
            with self.subTest(bad=wrong), self.assertRaises(ValueError):
                self.trade.validate(self.params(**gift, **wrong))

    def test_marcos_das_atividades(self):
        """O "[done] delivery" do FRLG e o anúncio do presente do SwSh viram mensagens próprias."""
        def find(line):
            if (m := self.trade.DONE.search(line)) and m.group(1) != "trade":
                return ("done", m.group(1), int(m.group(2)))
            for pattern, kind in self.trade.MILESTONES:
                if found := pattern.search(line):
                    return (kind, int(found.group(1)) if found.groups() else None)
            return None
        self.assertEqual(find("[done] delivery 1 complete"), ("done", "delivery", 1))
        self.assertEqual(find("advertising comm id 0x0100abcd"), ("advertising", None))
        self.assertEqual(find("served 42 advertisements"), ("served", 42))
        self.assertIsNone(find("[done] trade 1 complete"))

    def test_marcos_e_recebidos(self):
        """O "[done]" manda o arquivo mais novo da pasta de recebidos; cada linha vai para o log sem chaves."""
        received = os.path.join(self.tmp.name, "rec")
        os.makedirs(received)
        events, log = [], io.StringIO()
        got = self.trade.Received(received)

        def on_line(line):
            if m := self.trade.LINK.search(line):
                events.append(("link", m.group(1)))
            elif m := self.trade.DONE.search(line):
                events.append(("done", int(m.group(2)), got.take()))

        out = self.trade.Lines(on_line, log)
        out.write("[link] waiting\n[sv] key 00112233445566778899aabbccddeeff00\n[link] joi")
        out.write("ned\n")
        with open(os.path.join(received, "sv-1.pk9"), "wb") as f:
            f.write(b"um")
        out.write("[done] trade 1 complete\n")
        time.sleep(0.01)
        with open(os.path.join(received, "sv-1-2.pk9"), "wb") as f:
            f.write(b"dois")
        out.write("[done] trade 2 complete\n")
        self.assertEqual(events, [("link", "waiting"), ("link", "joined"), ("done", 1, b"um"), ("done", 2, b"dois")])
        self.assertIn("[hex]", log.getvalue())
        self.assertNotIn("00112233445566778899", log.getvalue())

    def test_comando_de_cada_jogo(self):
        python = pokeldn_python()
        if python is None or not os.path.isfile(os.path.join(REPO, "engines", "pokeldn", "pokeldn", "__init__.py")):
            self.skipTest("sem o Python com as dependências do pokeldn (scripts/build-worker.sh) ou sem o submódulo")
        root = os.path.join(self.tmp.name, "root")
        subprocess.run([os.path.join(REPO, "scripts", "stage-pokeldn.sh"), os.path.join(root, "pokeldn")], check=True)
        code = f"""
            import json, os, sys, tempfile
            sys.path.insert(0, {WORKER!r})
            from traderock_worker import pokeldn_env, pokeldn_trade
            work = tempfile.mkdtemp()
            engine = pokeldn_env.prepare({root!r}, os.path.join(work, "data"))
            out = {{}}
            for game, code in [("sv", "12345678"), ("swsh", "12345678"), ("za", None), ("pla", "12345678"),
                               ("bdsp", "12345678"), ("lgpe", "0,4,9"), ("frlg", None)]:
                p = {{"game": game, "offers": ["eA==", "eA=="], "code": code, "tradeBack": game == "sv", "keys": {self.keys!r},
                     "trainer": {{"name": "Ana", "tid": 123456 if game != "frlg" else 12345, "sid": 1234}},
                     "trades": 1, "version": "leafgreen"}}
                script, argv, _ = pokeldn_trade.command(p, engine, work)
                out[game] = [os.path.basename(script), argv]
            print(json.dumps(out))
            """
        r = subprocess.run([python, "-c", textwrap.dedent(code)], capture_output=True, text=True, timeout=120,
                           env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
        self.assertEqual(r.returncode, 0, r.stderr)
        out = json.loads(r.stdout.strip().splitlines()[-1])
        self.assertEqual(out["sv"][0], "sv_host.py")
        self.assertEqual(out["lgpe"][0], "lgpe_host.py")
        self.assertEqual(out["frlg"][0], "frlg_trade_host.py")
        for game, (_, argv) in out.items():
            with self.subTest(game=game):
                self.assertNotIn("--capture", argv)  # leva chaves de sessão (D21)
                self.assertNotIn("--fresh-pid", argv)  # passaria pelo ajudante PKHeX do pokeldn
                self.assertIn("--keys", argv)
        self.assertIn("--trade-back", out["sv"][1])
        self.assertIn("pikachu,squirtle,diglett", out["lgpe"][1])
        self.assertIn("Ana:15040:18831", out["bdsp"][1])  # 123456/1234 viram TID16 15040 e SID16 18831
        self.assertEqual(out["frlg"][1][-out["frlg"][1][::-1].index("--trades")], "1")
        self.assertIn("leafgreen", out["frlg"][1])

    def test_comando_do_mystery_gift(self):
        python = pokeldn_python()
        if python is None or not os.path.isfile(os.path.join(REPO, "engines", "pokeldn", "pokeldn", "__init__.py")):
            self.skipTest("sem o Python com as dependências do pokeldn (scripts/build-worker.sh) ou sem o submódulo")
        root = os.path.join(self.tmp.name, "root")
        subprocess.run([os.path.join(REPO, "scripts", "stage-pokeldn.sh"), os.path.join(root, "pokeldn")], check=True)
        code = f"""
            import json, os, sys, tempfile
            sys.path.insert(0, {WORKER!r})
            from traderock_worker import pokeldn_env, pokeldn_trade
            work = tempfile.mkdtemp()
            engine = pokeldn_env.prepare({root!r}, os.path.join(work, "data"))
            base = {{"activity": "gift", "keys": {self.keys!r}, "trainer": {{"name": "Ana", "tid": 12345, "sid": 54321}}}}
            out = {{}}
            for name, p in [("frlg", {{"game": "frlg", "version": "leafgreen", "gift": {{"mode": "preset", "preset": "celebi"}}}}),
                            ("frlg-flags", {{"game": "frlg", "version": "firered", "gift": {{"mode": "preset", "preset": "speed-2"}}}}),
                            ("swsh", {{"game": "swsh", "phy": "phy1", "gift": {{"mode": "event", "event": "pokemon-go-transfer-gift-melmetal"}}}}),
                            ("frlg-event-pokemon", {{"game": "frlg", "version": "firered", "gift": {{"mode": "preset", "preset": "event-aura-mew"}}}}),
                            ("swsh-unknown", {{"game": "swsh", "gift": {{"mode": "event", "event": "nao-existe"}}}})]:
                try:
                    script, argv, _ = pokeldn_trade.command({{**base, **p}}, engine, work)
                    gift = argv[argv.index("--gift-file") + 1] if "--gift-file" in argv else None
                    out[name] = [os.path.basename(script), argv, gift is not None and os.path.isfile(gift)]
                except ValueError as e:
                    out[name] = ["recusado", str(e)]
            print(json.dumps(out))
            """
        r = subprocess.run([python, "-c", textwrap.dedent(code)], capture_output=True, text=True, timeout=120,
                           env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
        self.assertEqual(r.returncode, 0, r.stderr)
        out = json.loads(r.stdout.strip().splitlines()[-1])
        script, argv, written = out["frlg"]
        self.assertEqual(script, "frlg_mg_host.py")
        self.assertEqual(argv[argv.index("--gift") + 1], "celebi")  # presente do FRLG vai como flag
        self.assertIn("leafgreen", argv)
        self.assertIn("12345:54321", argv)
        self.assertEqual(out["frlg-flags"][1][out["frlg-flags"][1].index("--gift") + 1], "speed-2")
        script, argv, written = out["swsh"]
        self.assertEqual(script, "swsh_gift_host.py")
        self.assertTrue(written)  # o evento oficial vira .pokegift, gravado pelo prepare do pokeldn
        self.assertEqual(argv[argv.index("--phy") + 1], "phy1")
        for name in ("frlg", "frlg-flags", "swsh"):
            self.assertNotIn("--save-to-board", out[name][1])
            self.assertNotIn("--capture", out[name][1])
        self.assertEqual(out["frlg-event-pokemon"][0], "recusado")
        self.assertEqual(out["swsh-unknown"][0], "recusado")


if __name__ == "__main__":
    unittest.main()
