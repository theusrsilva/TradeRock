"""Estado do rádio e configuração de rede.

O motor (frlg-ldn-trade) desliga a interface normal da placa escolhida para ocupar o canal e não a
religa no fim. Antes de cada operação o worker anota como estavam as interfaces do phy e, no fim,
apaga as interfaces virtuais do LDN e devolve cada uma ao estado anterior (ligada e gerenciada pelo
NetworkManager, se era assim).
"""

import os
import re
import subprocess
from dataclasses import dataclass

# Nomes das interfaces virtuais: as do motor (ldn, ldn-mon, ldn-tap, ldnclient, ldnc0..9, ldnrx) e
# as do teste da placa (ldntest e ldntestap). Todas começam com "ldn", o que casa com o NetworkManager e o udev.
LDN_PREFIX = "ldn"
TEST_VIF = "ldntest"
AP_VIF = "ldntestap"

NM_CONF_PATH = "/etc/NetworkManager/conf.d/zz-ldn-unmanaged.conf"
NM_CONF = """\
# TradeRock: o NetworkManager não deve gerenciar as interfaces virtuais do LDN.
# Se ele adotar a interface criada a cada entrada na sala, o wpa_supplicant se registra para os
# quadros de ação antes do motor e a entrada autentica, mas nunca recebe o IP.
[device-traderock-ldn]
match-device=interface-name:ldn*
managed=0
"""

LINK_PATH = "/etc/systemd/network/10-traderock-ldn.link"
LINK_CONF = """\
# TradeRock: manter os nomes que o motor LDN dá às interfaces virtuais.
# Em adaptadores USB, 73-usb-net-by-mac.link renomeia toda interface para wlx<mac>, inclusive
# ldnclient; o motor então configura um nome que não existe mais.
[Match]
OriginalName=ldn*

[Link]
NamePolicy=
MACAddressPolicy=none
"""

# iw e ip embutidos (scripts/build-nettools.sh) ficam em <instalação>/bin e vêm antes dos do sistema:
# o motor chama os dois pelo nome, então usa os nossos sem precisar de nada instalado.
INSTALL_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BUNDLED_BIN = os.path.join(INSTALL_ROOT, "bin")
SAFE_PATH = BUNDLED_BIN + ":/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"


def run(cmd):
    """Roda um comando do sistema (sem shell). Retorna (código, saída)."""
    try:
        done = subprocess.run(cmd, capture_output=True, text=True, timeout=20,
                              env={"PATH": SAFE_PATH, "LANG": "C.UTF-8"})
        return done.returncode, done.stdout
    except (OSError, subprocess.TimeoutExpired) as e:
        return 127, str(e)


def which(tool):
    for d in SAFE_PATH.split(":"):
        p = os.path.join(d, tool)
        if os.access(p, os.X_OK):
            return p
    return None


# Linha de canal do `iw phy <phy> info`: "* 2412.0 MHz [1] (20.0 dBm)" ou "* 5180.0 MHz [36] (disabled)".
IW_CHANNEL_RE = re.compile(r"^\s*\*\s+[\d.]+\s+MHz\s+\[(\d+)\](.*)$")


def supported_channels(phy, wanted, runner=run):
    """Os canais de `wanted` que a placa sintoniza, na mesma ordem.

    O ldn.scan da troca aborta no primeiro canal que a placa recusa (placa só de 2,4 GHz nos canais
    de 5 GHz), mesmo com a sala já vista num canal anterior. Se o `iw` falhar ou nada sobrar,
    devolve `wanted` como está: o motor mostra o erro de sempre.
    """
    code, out = runner(["iw", "phy", phy, "info"])
    if code != 0:
        return list(wanted)
    enabled = set()
    for line in out.splitlines():
        m = IW_CHANNEL_RE.match(line)
        if m and "disabled" not in m.group(2):
            enabled.add(int(m.group(1)))
    usable = [c for c in wanted if c in enabled]
    return usable or list(wanted)


@dataclass(frozen=True)
class IfaceState:
    name: str
    up: bool
    nm_managed: bool | None   # None: NetworkManager ausente


class Radio:
    def __init__(self, sysroot="/", runner=run):
        self.sys = os.path.join(sysroot, "sys")
        self.run = runner

    def phy_exists(self, phy):
        return os.path.isdir(os.path.join(self.sys, "class", "ieee80211", phy))

    def interfaces(self, phy):
        """Interfaces de rede do phy (pelo link phy80211 de cada uma em /sys/class/net)."""
        net = os.path.join(self.sys, "class", "net")
        found = []
        try:
            names = sorted(os.listdir(net))
        except OSError:
            return found
        for name in names:
            link = os.path.join(net, name, "phy80211")
            if os.path.islink(link) and os.path.basename(os.readlink(link)) == phy:
                found.append(name)
        return found

    def _is_up(self, iface):
        try:
            with open(os.path.join(self.sys, "class", "net", iface, "flags")) as f:
                return int(f.read().strip(), 16) & 0x1 == 1
        except (OSError, ValueError):
            return False

    def _nm_states(self):
        code, out = self.run(["nmcli", "-t", "-f", "DEVICE,STATE", "device", "status"])
        if code != 0:
            return None
        states = {}
        for line in out.splitlines():
            device, _, state = line.rpartition(":")
            if device:
                states[device.replace("\\:", ":")] = state
        return states

    def snapshot(self, phy):
        nm = self._nm_states()
        return [IfaceState(i, self._is_up(i), None if nm is None else nm.get(i, "unmanaged") != "unmanaged")
                for i in self.interfaces(phy) if not i.startswith(LDN_PREFIX)]

    def take(self, snapshot):
        """Tira as interfaces normais do phy do ar (como o motor faz), para o teste da placa."""
        for s in snapshot:
            if s.nm_managed:
                self.run(["nmcli", "device", "set", s.name, "managed", "no"])
            self.run(["ip", "link", "set", s.name, "down"])

    def cleanup_vifs(self):
        """Apaga toda interface virtual do LDN que tenha sobrado."""
        net = os.path.join(self.sys, "class", "net")
        try:
            names = os.listdir(net)
        except OSError:
            names = []
        removed = []
        for name in sorted(names):
            if name.startswith(LDN_PREFIX):
                self.run(["iw", "dev", name, "del"])
                removed.append(name)
        return removed

    def restore(self, snapshot):
        self.cleanup_vifs()
        for s in snapshot:
            if s.up:
                self.run(["ip", "link", "set", s.name, "up"])
            if s.nm_managed:
                self.run(["nmcli", "device", "set", s.name, "managed", "yes"])


class NetworkConfig:
    """Arquivos que tiram as interfaces do LDN do NetworkManager e do renomeador do udev."""

    def __init__(self, root="/", runner=run):
        self.root = root
        self.run = runner

    def _path(self, path):
        return os.path.join(self.root, path.lstrip("/"))

    def _matches(self, path, content):
        try:
            with open(self._path(path)) as f:
                return f.read() == content
        except OSError:
            return False

    def status(self):
        return {"networkManager": self._matches(NM_CONF_PATH, NM_CONF),
                "udevLink": self._matches(LINK_PATH, LINK_CONF)}

    def install(self):
        for path, content in ((NM_CONF_PATH, NM_CONF), (LINK_PATH, LINK_CONF)):
            target = self._path(path)
            os.makedirs(os.path.dirname(target), mode=0o755, exist_ok=True)
            tmp = target + ".traderock-tmp"
            fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o644)
            # O worker roda com umask 077 (server.main), que deixaria 600; o app, sem root, precisa
            # ler os arquivos para saber se a configuração está feita (LinuxProbe).
            os.fchmod(fd, 0o644)
            with os.fdopen(fd, "w") as f:
                f.write(content)
            os.replace(tmp, target)
        # Recarrega a configuração (sem reiniciar: a conexão atual não cai).
        if self.run(["nmcli", "general", "reload", "conf"])[0] != 0:
            self.run(["systemctl", "reload", "NetworkManager"])
        self.run(["udevadm", "control", "--reload"])
        return self.status()
