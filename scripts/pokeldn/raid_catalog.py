"""Catálogo das raids de evento para o TradeRock (catalog/raids.json), lido pelo próprio pokeldn na cópia embutida
das galerias (assets/raid-events/raid-events.zip, scripts/fetch-raid-events.sh; D26).

- Sword/Shield: cada evento (Wild Area Event) com os chefes que uma raid hospedada pode mostrar
  (swsh/raid_event.hostable), como [valor do campo "raideventboss" ("versão:estrelas:número:área"), descrição].
- Scarlet/Violet: cada evento com os grupos de cada versão (sv/raid_event.dens) e as etapas da história que
  servem para cada grupo (progresses).

Uso (na cópia do pokeldn, com as dependências dele): python raid_catalog.py ZIP SAÍDA
"""
import json
import sys
import tempfile
import zipfile
from pathlib import Path

sys.path[:0] = ["vendor/LDN", "."]

from pokeldn.app import events_gallery  # noqa: E402
from pokeldn.sv import raid_event as sv_event  # noqa: E402
from pokeldn.swsh import raid_event as swsh_event  # noqa: E402


def swsh(root):
    out = []
    for entry in events_gallery.index(root, "swsh"):
        event = events_gallery.load(entry.key, root, "swsh")
        # "valor" = "versão:estrelas:número:área" (a tela separa versão, estrelas e área dele).
        bosses = [[f"{v}:{stars}:{n}:{area}", swsh_event.describe(boss, stars - 1)]
                  for v, stars, n, area, boss in swsh_event.hostable(event)]
        if bosses:
            out.append({"key": entry.key, "title": entry.title, "bosses": bosses})
    return out


def sv(root):
    out = []
    for entry in events_gallery.index(root, "sv"):
        event = events_gallery.load(entry.key, root, "sv")
        versions = {}
        for version in sv_event.versions(event):
            versions[version] = [{"group": d.group, "label": d.label, "content": d.content,
                                  "progress": sv_event.progresses(event, version, d.group)}
                                 for d in sv_event.dens(event, version)]
        if versions:
            out.append({"key": entry.key, "title": entry.title, "species": list(entry.species), "versions": versions})
    return out


def main(archive, path):
    with tempfile.TemporaryDirectory() as tmp:
        zipfile.ZipFile(archive).extractall(tmp)
        catalog = {"swsh": swsh(Path(tmp) / "swsh"), "sv": sv(Path(tmp) / "sv")}
    with open(path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, separators=(",", ":"))
        f.write("\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
