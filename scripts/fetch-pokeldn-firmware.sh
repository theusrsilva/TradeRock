#!/usr/bin/env bash
# Baixa o firmware do ESP32 do pokeldn (imagens "merged", gravadas em 0x0) do Release publicado e confere com o
# assets/firmware/pokeldn/SHA256SUMS.txt (copiado do SHA256SUMS do Release). O resultado fica em assets/firmware/pokeldn (no
# repositório) e é embutido no app; o app nunca baixa nada (D17). Rode de novo só ao atualizar o submódulo.
# Firmware 1.9.1 = firmware/esp32/version.txt do commit travado do submódulo (D21, D23).
set -euo pipefail
cd "$(dirname "$0")/.."

TAG=v0.49.1
DIR=assets/firmware/pokeldn
FILES=(pokeldn-radio.bin pokeldn-radio-s3.bin pokeldn-radio-c3.bin pokeldn-radio-c6.bin)
mkdir -p "$DIR"

for f in "${FILES[@]}"; do
  [[ -s $DIR/$f ]] || curl -fsSL -o "$DIR/$f" "https://github.com/Decryptu/pokeldn/releases/download/$TAG/$f"
done
(cd "$DIR" && sha256sum -c --quiet SHA256SUMS.txt)
echo "firmware do pokeldn $TAG conferido em $DIR"
