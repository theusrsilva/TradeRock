"""Catálogo de presentes do Mystery Gift para o TradeRock (catalog/gifts.json), lido do próprio pokeldn.

Roda na cópia do pokeldn com os patches (scripts/test-pokeldn.sh), com TRADEROCK=1. Ficam de fora os
presentes que pedem o ajudante PKHeX do pokeldn (Pokémon de evento do FRLG), os que leem o save e os que
têm opções ou dependem deles (D26). O worker aplica o mesmo filtro (pokeldn_trade.gift_value). Uso: python gift_catalog.py SAÍDA
"""
import json
import os
import sys

os.environ["TRADEROCK"] = "1"
sys.path[:0] = ["vendor/LDN", "."]

from pokeldn.app import gift_builder  # noqa: E402

# "Game boosts": guardar e restaurar melhorias, que dependem do preset com opções ("boosts").
SKIP_GROUPS = {"frlg": {"Event Pokemon", "10th Anniversary Pokemon", "Read the save", "Game boosts"}}


def presets(game):
    out = []
    for p in gift_builder.module(game).PRESETS:
        if p.group in SKIP_GROUPS.get(game, ()) or getattr(p, "arguments", None):
            continue
        out.append({"key": p.key, "group": p.group, "label": p.label, "summary": p.summary,
                    "when": getattr(p, "when", "") or ""})
    return out


def events(game):
    builder = gift_builder.module(game)
    if not hasattr(builder, "OFFICIAL"):
        return []
    return [{"key": e["key"], "group": e["group"], "label": e["label"], "summary": e["summary"]}
            for e in builder.OFFICIAL.load()]


def main(path):
    catalog = {game: {"presets": presets(game), "events": events(game)} for game in ("frlg", "swsh")}
    with open(path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, indent=1)
        f.write("\n")


if __name__ == "__main__":
    main(sys.argv[1])
