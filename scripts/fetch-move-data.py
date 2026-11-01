#!/usr/bin/env python3
"""Gera worker/traderock_worker/data/moves.json, a tabela de golpes da mini IA do parceiro (D31).

Baixa o moves.csv do PokeAPI (data/v2/csv, licença BSD-3) num commit fixo e guarda, para os golpes até a
Gen 8 (Sword/Shield), [tipo, força, precisão, categoria]:
- tipo na ordem do jogo (0 Normal ... 17 Fairy; o PokeAPI conta de 1);
- força e precisão nulas quando variam (Low Kick, Gyro Ball...) ou não existem;
- categoria 0 status, 1 físico, 2 especial.

Uso: scripts/fetch-move-data.py  (precisa de internet só aqui; o app não baixa nada).
"""
import csv
import io
import json
import os
import urllib.request

COMMIT = "ed5c11ee134efdcd4985a32dfa749f99802e5c8d"
URL = f"https://raw.githubusercontent.com/PokeAPI/pokeapi/{COMMIT}/data/v2/csv/moves.csv"
MAX_GEN = 8
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "worker", "traderock_worker", "data", "moves.json")


def number(text):
    return int(text) if text else None


def main():
    with urllib.request.urlopen(URL, timeout=60) as response:
        rows = list(csv.DictReader(io.StringIO(response.read().decode("utf-8"))))
    moves = {}
    for row in rows:
        move = int(row["id"])
        if move >= 10000 or int(row["generation_id"]) > MAX_GEN:
            continue  # golpes "shadow" e Z/Max (ids altos) e os da Gen 9
        moves[str(move)] = [int(row["type_id"]) - 1, number(row["power"]), number(row["accuracy"]),
                            int(row["damage_class_id"]) - 1]
    data = {"source": f"PokeAPI data/v2/csv/moves.csv ({COMMIT}), BSD-3-Clause", "moves": moves}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, separators=(",", ":"), sort_keys=True)
        f.write("\n")
    print(f"{len(moves)} golpes em {os.path.normpath(OUT)}")


if __name__ == "__main__":
    main()
