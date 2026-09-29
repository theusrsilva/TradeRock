#!/usr/bin/env bash
# Monta o worker do TradeRock numa pasta de staging (build/worker/stage), pronta para instalar como
# root com scripts/install-worker.sh (desenvolvimento) ou empacotar no .deb/AppImage (Entrega 8).
#
# Conteúdo:
#   python/  Python 3.12 do python-build-standalone (checksum conferido) + dependências do motor
#            instaladas com hash fixo (worker/requirements.lock)
#   engine/  frlg-ldn-trade do submódulo + patches de engines/patches/frlg-ldn-trade
#   worker/  o worker (worker/traderock_worker)
#   BUILD-INFO.json
#
# Nenhuma chave, ROM ou dado do usuário entra aqui.
set -euo pipefail
cd "$(dirname "$0")/.."

PY_RELEASE=20260929
PY_FILE="cpython-3.12.14+${PY_RELEASE}-x86_64-unknown-linux-gnu-install_only.tar.gz"
PY_SHA256=06c90b93f419b63371c18f20fed0558a1a901f6518c3c24f755077e048447e7f
CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/traderock"
STAGE=build/worker/stage

mkdir -p "$CACHE"
if [[ ! -f "$CACHE/$PY_FILE" ]]; then
  echo "==> baixando $PY_FILE"
  curl -fsSL -o "$CACHE/$PY_FILE.part" \
    "https://github.com/astral-sh/python-build-standalone/releases/download/$PY_RELEASE/${PY_FILE//+/%2B}"
  mv "$CACHE/$PY_FILE.part" "$CACHE/$PY_FILE"
fi
echo "$PY_SHA256  $CACHE/$PY_FILE" | sha256sum -c --quiet

rm -rf "$STAGE"
mkdir -p "$STAGE"
tar xzf "$CACHE/$PY_FILE" -C "$STAGE"

echo "==> dependências do motor (hash fixo)"
"$STAGE/python/bin/python3" -m pip install --quiet --disable-pip-version-check --no-warn-script-location \
  --require-hashes --no-deps --cache-dir "$CACHE/pip" -r worker/requirements.lock
# O pip não é necessário depois de instalar (e é menos código rodando como root).
"$STAGE/python/bin/python3" -m pip uninstall --quiet --yes pip >/dev/null

echo "==> enxugando o Python (sem interface gráfica, testes nem símbolos de depuração)"
PYLIB="$STAGE/python/lib/python3.12"
rm -rf "$PYLIB"/{test,idlelib,tkinter,turtledemo,ensurepip,lib2to3,pydoc_data} \
       "$PYLIB"/lib-dynload/_tkinter* "$STAGE/python/lib"/{tcl*,tk*,itcl*,thread*,libtcl*,libtk*} \
       "$STAGE/python/include" "$STAGE/python/share" "$STAGE/python/lib/pkgconfig" \
       "$STAGE/python/lib"/libpython3* "$STAGE/python/bin"/python3*-config
# (o binário python3 é estático: a libpython3.so é só para quem embute o Python, e tem ~115 MB)
find "$STAGE/python/bin" -type f ! -name 'python3*' -delete
find "$STAGE/python/bin" -type l ! -name 'python3*' -delete
# Só os módulos de extensão: o binário e a libpython do python-build-standalone têm versionamento
# de símbolos que o strip do binutils corrompe.
find "$STAGE/python/lib/python3.12" -type f -name '*.so' -exec strip --strip-debug {} + 2>/dev/null || true

echo "==> motor + patches"
mkdir -p "$STAGE/engine"
cp -r engines/frlg-ldn-trade/frlgtrade.py engines/frlg-ldn-trade/frlgsim \
      engines/frlg-ldn-trade/LICENSE engines/frlg-ldn-trade/README.md "$STAGE/engine/"
patches=()
for p in engines/patches/frlg-ldn-trade/*.patch; do
  patch --quiet -p0 -d "$STAGE/engine" < "$p"
  patches+=("\"$(basename "$p")\"")
done
cp engines/patches/frlg-ldn-trade/README.md "$STAGE/engine/PATCHES.md"

echo "==> worker"
mkdir -p "$STAGE/worker"
cp worker/run.py "$STAGE/worker/"
cp -r worker/traderock_worker "$STAGE/worker/"
find "$STAGE/engine" "$STAGE/worker" -name __pycache__ -prune -exec rm -rf {} +
"$STAGE/python/bin/python3" -m compileall -q "$STAGE/engine" "$STAGE/worker"

commit=$(git -C engines/frlg-ldn-trade rev-parse HEAD)
cat > "$STAGE/BUILD-INFO.json" <<JSON
{
  "engineCommit": "$commit",
  "patches": [$(IFS=,; echo "${patches[*]}")],
  "python": "${PY_FILE%%-x86_64*}"
}
JSON
echo "==> pronto: $STAGE ($(du -sh "$STAGE" | cut -f1))"
