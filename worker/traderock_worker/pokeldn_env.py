"""Ambiente comum dos processos que rodam o pokeldn (D21, D23).

- Trava de rede (netguard) antes de importar qualquer coisa do pokeldn.
- `TRADEROCK=1` liga os patches de engines/patches/pokeldn: sem internet, sem plugins, oferta sem o ajudante PKHeX.
- Pasta de dados própria (`POKELDN_DATA`), apagada pelo chamador: nada do pokeldn fica no perfil da pessoa.
- A cópia do pokeldn (`<raiz>/pokeldn`) e o LDN vendorizado dela vêm antes do `ldn` do PyPI usado pelo FRLG.
"""

import os
import re
import sys

from . import netguard

# Sequências hexadecimais longas (chaves de sessão derivadas que o pokeldn imprime, D21) saem do log.
_HEX = re.compile(r"\b(?:[0-9a-fA-F]{2}[ :]?){16,}\b")


def redact(line):
    return _HEX.sub("[hex]", line)


def prepare(root, data_dir, radio=None):
    netguard.install()
    os.environ["TRADEROCK"] = "1"
    os.environ["POKELDN_DATA"] = data_dir
    os.environ["POKELDN_MANAGED_RUN"] = "1"
    os.environ.pop("POKELDN_PLUGINS", None)
    if radio:
        os.environ["POKELDN_RADIO"] = radio
    else:
        os.environ.pop("POKELDN_RADIO", None)
    engine = os.path.join(root, "pokeldn")
    for path in (os.path.join(engine, "vendor", "LDN"), engine):
        if path not in sys.path:
            sys.path.insert(0, path)
    return engine
