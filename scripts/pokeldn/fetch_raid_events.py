"""Baixa as raids de evento da EventsGallery do Project Pokémon num commit fixo, com o próprio pokeldn
(pokeldn/app/events_gallery.py), e grava um .zip determinístico para o app embutir (D26). Só no
desenvolvimento: o app nunca baixa nada (D17).

Uso (na cópia do pokeldn, com as dependências dele): python fetch_raid_events.py SAÍDA.zip
"""
import json
import os
import sys
import tempfile
import zipfile
from pathlib import Path

sys.path[:0] = ["vendor/LDN", "."]

from pokeldn.app import events_gallery  # noqa: E402

# Último commit da galeria que mexeu nas raids de cada jogo (events_gallery._latest), travado.
PINNED = {
    "sv": ("bc61f78a239e34734f6db7d95a6cc674874e4c72", "2026-09-06T21:05:05Z"),
    "swsh": ("0595d451082f531a08389d4c473f3dc08263a604", "2022-11-01T19:45:10Z"),
}
STAMP = (2020, 1, 1, 0, 0, 0)


def main(out):
    events_gallery._newest = lambda game: PINNED[game]
    with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for game in sorted(PINNED):
            root = Path(tmp) / game
            events_gallery.download(root, game=game)
            for path in sorted(p for p in root.rglob("*") if p.is_file()):
                info = zipfile.ZipInfo(f"{game}/{path.relative_to(root).as_posix()}", STAMP)
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o644 << 16
                z.writestr(info, path.read_bytes())
        info = zipfile.ZipInfo("PINNED.json", STAMP)
        z.writestr(info, json.dumps(PINNED, indent=1, sort_keys=True) + "\n")


if __name__ == "__main__":
    main(os.path.abspath(sys.argv[1]))
