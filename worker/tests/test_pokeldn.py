"""Testes da integração com o pokeldn (D21): patches do modo TradeRock e trava de rede."""

import os
import subprocess
import sys
import tempfile
import textwrap
import unittest

TESTS = os.path.dirname(os.path.abspath(__file__))
WORKER = os.path.dirname(TESTS)
REPO = os.path.dirname(WORKER)


def run_python(code, *, cwd=None, env=None):
    full_env = {k: v for k, v in os.environ.items() if not k.startswith(("POKELDN_", "TRADEROCK"))}
    full_env.update(env or {})
    return subprocess.run([sys.executable, "-c", textwrap.dedent(code)], cwd=cwd, env=full_env,
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

    def run_engine(self, code, traderock):
        env = {"POKELDN_DATA": os.path.join(self.tmp.name, "data")}
        if traderock:
            env["TRADEROCK"] = "1"
        return run_python("import sys; sys.path.insert(0, '.')\n" + textwrap.dedent(code), cwd=self.engine, env=env)

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


if __name__ == "__main__":
    unittest.main()
