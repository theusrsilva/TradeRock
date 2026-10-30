#!/usr/bin/env bash
# Copia o pokeldn (engines/pokeldn) para DEST e aplica engines/patches/pokeldn (D21).
#
#   scripts/stage-pokeldn.sh DEST          só o que o worker usa: pokeldn/, bin/, o LDN vendorizado,
#                                          config/, a versão do firmware, as licenças e as raids de evento
#                                          (assets/raid-events/raid-events.zip em DEST/traderock-raid-events.zip;
#                                          o worker extrai só o evento escolhido, D26)
#   scripts/stage-pokeldn.sh DEST --full   o repositório inteiro (sem .git), para rodar os testes dele
#
# O submódulo fica intocado. Se um patch deixar de aplicar, o script falha.
set -euo pipefail
cd "$(dirname "$0")/.."

DEST=${1:?uso: scripts/stage-pokeldn.sh DEST [--full]}
SRC=engines/pokeldn
[[ -f $SRC/pokeldn/__init__.py ]] || { echo "submódulo engines/pokeldn ausente (git submodule update --init)" >&2; exit 1; }

rm -rf "$DEST"
mkdir -p "$DEST"
if [[ ${2:-} == --full ]]; then
  tar -C "$SRC" --exclude=.git -cf - . | tar -C "$DEST" -xf -
  # Alguns testes do pokeldn leem o próprio checkout (git ls-files, títulos em prévia): repositório descartável.
  git -C "$DEST" init -q
  git -C "$DEST" add -A
  git -C "$DEST" -c user.name=traderock -c user.email=traderock@localhost commit -q -m "pokeldn $(git -C "$SRC" rev-parse --short HEAD)"
else
  cp -r "$SRC"/{pokeldn,bin,config,LICENSE,README.md} "$DEST/"
  mkdir -p "$DEST/vendor/LDN" "$DEST/firmware/esp32"
  cp -r "$SRC"/vendor/LDN/{ldn,LICENSE,UPSTREAM.md} "$DEST/vendor/LDN/"
  cp "$SRC/firmware/esp32/version.txt" "$DEST/firmware/esp32/"
  if [[ -f assets/raid-events/raid-events.zip ]]; then
    (cd assets/raid-events && sha256sum -c --quiet SHA256SUMS)
    cp assets/raid-events/raid-events.zip "$DEST/traderock-raid-events.zip"
  fi
fi
for p in engines/patches/pokeldn/*.patch; do
  patch --quiet -p0 -d "$DEST" < "$p"
done
cp engines/patches/pokeldn/README.md "$DEST/PATCHES.md"
find "$DEST" -name __pycache__ -prune -exec rm -rf {} +
