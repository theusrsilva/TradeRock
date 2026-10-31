#!/usr/bin/env bash
# Baixa as raids de evento (EventsGallery do Project Pokémon, Scarlet/Violet e Sword/Shield) num commit fixo com o
# próprio pokeldn e grava assets/raid-events/raid-events.zip, conferido por assets/raid-events/SHA256SUMS (D26).
# Roda só quando o conjunto muda; o resultado fica no repositório e o app nunca baixa nada (D17).
#   scripts/fetch-raid-events.sh           confere o .zip
#   scripts/fetch-raid-events.sh --update  baixa de novo e regrava o SHA256SUMS (ao trocar os commits)
set -euo pipefail
cd "$(dirname "$0")/.."
REPO=$PWD
DIR=assets/raid-events

if [[ ${1:-} == --update ]]; then
  CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/traderock"
  reqs=$(cat engines/pokeldn/requirements.txt engines/pokeldn/requirements-dev.txt | sha256sum | cut -c1-12)
  VENV="$CACHE/pokeldn-venv-$reqs"
  [[ -x $VENV/bin/python ]] || { echo "rode scripts/test-pokeldn.sh antes (cria o Python do pokeldn)" >&2; exit 1; }
  work=$(mktemp -d)
  trap 'rm -rf "$work"' EXIT
  scripts/stage-pokeldn.sh "$work/pokeldn" --full >/dev/null
  mkdir -p "$DIR"
  (cd "$work/pokeldn" && POKELDN_DATA="$work/data" "$VENV/bin/python" "$REPO/scripts/pokeldn/fetch_raid_events.py" "$REPO/$DIR/raid-events.zip")
  (cd "$DIR" && sha256sum raid-events.zip > SHA256SUMS)
fi
(cd "$DIR" && sha256sum -c --quiet SHA256SUMS)
echo "raids de evento conferidas em $DIR"
