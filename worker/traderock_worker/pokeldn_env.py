"""Ambiente comum dos processos que rodam o pokeldn (D21, D23).

- Trava de rede (netguard) antes de importar qualquer coisa do pokeldn.
- `TRADEROCK=1` liga os patches de engines/patches/pokeldn: sem internet, sem plugins, oferta sem o ajudante PKHeX.
- Pasta de dados própria (`POKELDN_DATA`), apagada pelo chamador: nada do pokeldn fica no perfil da pessoa.
- A cópia do pokeldn (`<raiz>/pokeldn`) e o LDN vendorizado dela vêm antes do `ldn` do PyPI usado pelo FRLG.
- As raids de evento vêm do .zip embutido (`<raiz>/pokeldn/traderock-raid-events.zip`, D26), não da pasta de dados:
  o pokeldn as baixaria da internet. Só o evento escolhido é extraído, na pasta da sessão.
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
    if radio and radio != "lan":  # LAN (D24): sem rádio, o pokeldn fala UDP pela rede de casa
        os.environ["POKELDN_RADIO"] = radio
    else:
        os.environ.pop("POKELDN_RADIO", None)
    engine = os.path.join(root, "pokeldn")
    for path in (os.path.join(engine, "vendor", "LDN"), engine):
        if path not in sys.path:
            sys.path.insert(0, path)
    return engine


def use_bundled_raid_events(engine, work, game, key):
    """Extrai o evento `key` do .zip embutido para `work` e aponta a galeria do pokeldn
    (pokeldn/app/events_gallery.py) para lá. -> False se o .zip não tem o evento."""
    import zipfile
    from pathlib import Path
    from pokeldn.app import events_gallery
    root = Path(work) / "raid-events"
    prefix = f"{game}/{key}/"
    found = False
    with zipfile.ZipFile(os.path.join(engine, "traderock-raid-events.zip")) as z:
        for info in z.infolist():
            name = info.filename
            if info.is_dir() or not (name.startswith(prefix) or name == f"{game}/gallery.json"):
                continue
            parts = name.split("/")
            if ".." in parts or "" in parts:
                continue
            found |= name.startswith(prefix)
            # Os nomes das pastas dos eventos passam de 150 caracteres: no Windows, o caminho longo (\\?\).
            target = events_gallery.long_path(root.joinpath(*parts))
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with z.open(info) as source, open(target, "wb") as out:
                out.write(source.read())
    events_gallery.location = lambda game="sv": root / game
    return found
