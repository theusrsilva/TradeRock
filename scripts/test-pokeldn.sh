#!/usr/bin/env bash
# Roda os testes offline do pokeldn (engines/pokeldn) na cópia com os patches do TradeRock (D21).
# O ajudante PKHeX do pokeldn (services/pkhex, .NET 10) é compilado só para os testes: o worker não o usa.
# Testes que precisam só do flet (interface gráfica do pokeldn) contam como "skipped".
set -euo pipefail
cd "$(dirname "$0")/.."

CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/traderock"
BUILD="$CACHE/pokeldn-test"   # fora do repositório: o Directory.Build.props do TradeRock não vale para o pokeldn
scripts/stage-pokeldn.sh "$BUILD/src" --full

reqs=$(cat engines/pokeldn/requirements.txt engines/pokeldn/requirements-dev.txt | sha256sum | cut -c1-12)
VENV="$CACHE/pokeldn-venv-$reqs"
if [[ ! -x $VENV/bin/python ]]; then
  echo "==> venv de teste do pokeldn"
  python3 -m venv "$VENV.part"
  (cd "$BUILD/src" && "$VENV.part/bin/pip" install --quiet --disable-pip-version-check -r requirements-dev.txt)
  mv "$VENV.part" "$VENV"
fi

echo "==> ajudante PKHeX do pokeldn (só para os testes)"
dotnet build --nologo -v q -c Release "$BUILD/src/services/pkhex" -o "$BUILD/pkhex" >/dev/null

echo "==> testes do pokeldn"
cd "$BUILD/src"
POKELDN_PKHEX="$BUILD/pkhex/pokeldn-pkhex" POKELDN_DATA="$BUILD/data" \
PYTHONPATH="$OLDPWD/scripts/pokeldn" \
  "$VENV/bin/python" -m pytest -q -p no:cacheprovider -p skip_gui -n "$(nproc)" "$@"
