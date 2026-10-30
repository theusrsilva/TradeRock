#!/usr/bin/env bash
# Baixa os sprites 387–1025 (Gen 4 a Gen 9) do PokeAPI/sprites num commit fixo para assets/sprites e confere
# com assets/sprites/SHA256SUMS. Os 1–386 continuam vindo do easyworld (engines/frlg-ldn-trade-esp32/assets/sprites).
# Roda só quando o conjunto muda (o resultado fica no repositório); o app nunca baixa nada (D17).
#   scripts/fetch-sprites.sh           baixa o que falta e confere
#   scripts/fetch-sprites.sh --update  baixa tudo e regrava o SHA256SUMS (ao trocar o commit)
set -euo pipefail
cd "$(dirname "$0")/.."

COMMIT=1dd55c0436d2a5b6e324c0b780e1b1b416fbfb64   # PokeAPI/sprites, 2026-10-28
FIRST=387
LAST=1025
DIR=assets/sprites
mkdir -p "$DIR"

seq "$FIRST" "$LAST" | while read -r n; do
  [[ ${1:-} != --update && -s $DIR/$n.png ]] || echo "$n"
done | xargs -r -P 8 -I{} curl -fsSL -o "$DIR/{}.png" \
  "https://raw.githubusercontent.com/PokeAPI/sprites/$COMMIT/sprites/pokemon/{}.png"

if [[ ${1:-} == --update ]]; then
  (cd "$DIR" && sha256sum $(seq -f '%g.png' "$FIRST" "$LAST") > SHA256SUMS)
fi
(cd "$DIR" && sha256sum -c --quiet SHA256SUMS)
echo "sprites $FIRST–$LAST conferidos em $DIR"
