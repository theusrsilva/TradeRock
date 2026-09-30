#!/usr/bin/env bash
# Monta o TradeRock para Windows x64 (só ESP32) em build/windows/app e, se o Inno Setup estiver
# disponível, o instalador dist/TradeRock-Setup-<versão>.exe.
#
#   scripts/package-windows.sh [versão] [--installer] [--tests]
#
# --tests também compila os testes para win-x64 em build/windows/tests (o CI roda no Windows).
#
# Roda no Linux (e no WSL): o app é compilado para win-x64 daqui mesmo. O instalador precisa do
# Inno Setup 6 (ISCC.exe), que só roda no Windows: no WSL ele é chamado pelo Windows; no CI, um job
# Windows compila packaging/windows/traderock.iss com a pasta montada aqui.
#
# Conteúdo de build/windows/app:
#   traderock.exe + .NET (self-contained) + frontend embutido + traderock.ico
#   esp32-runtime/python   CPython 3.12 do python-build-standalone para Windows (checksum conferido)
#                          + esptool 5.4.0 e dependências win_amd64 com hash fixo
#                          (worker/requirements-esp32-windows.lock; o esptool vem do requirements.lock)
#   esp32-runtime/worker   só o necessário para "run.py --esp32" (gravação do ESP32, roda como o usuário)
#   LICENSE, THIRD-PARTY-NOTICES.md
#
# Nenhuma chave, ROM ou dado do usuário entra aqui.
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=""
INSTALLER=0
TESTS=0
for arg in "$@"; do
  case "$arg" in
    --installer) INSTALLER=1 ;;
    --tests) TESTS=1 ;;
    -*) echo "opção desconhecida: $arg" >&2; exit 2 ;;
    *) VERSION=${arg#v} ;;
  esac
done
VERSION=${VERSION:-$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' Directory.Build.props)}
if [[ ! $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
  echo "versão inválida: $VERSION" >&2; exit 2
fi

PY_RELEASE=20260929
PY_LINUX="cpython-3.12.14+${PY_RELEASE}-x86_64-unknown-linux-gnu-install_only.tar.gz"
PY_LINUX_SHA256=06c90b93f419b63371c18f20fed0558a1a901f6518c3c24f755077e048447e7f
PY_WIN="cpython-3.12.14+${PY_RELEASE}-x86_64-pc-windows-msvc-install_only.tar.gz"
PY_WIN_SHA256=28728baf30b65e263f0b25c5a85be8226e7ab0d212fbadd6a8f0f796139fa804
CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/traderock"
BUILD=build/windows
APP=$BUILD/app
RUNTIME=$APP/esp32-runtime
DIST=dist

fetch() { # arquivo sha256
  if [[ ! -f "$CACHE/$1" ]]; then
    echo "==> baixando $1"
    curl -fsSL -o "$CACHE/$1.part" "https://github.com/astral-sh/python-build-standalone/releases/download/$PY_RELEASE/${1//+/%2B}"
    mv "$CACHE/$1.part" "$CACHE/$1"
  fi
  echo "$2  $CACHE/$1" | sha256sum -c --quiet
}

rm -rf "$BUILD"
mkdir -p "$BUILD" "$DIST" "$CACHE"

echo "==> app $VERSION (self-contained, win-x64)"
dotnet publish app/TradeRock.App -c Release -r win-x64 --self-contained \
  -p:BuildFrontend=true -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false \
  -o "$APP" --nologo -v quiet
[[ -f "$APP/traderock.exe" && -f "$APP/Photino.Native.dll" && -f "$APP/WebView2Loader.dll" && -f "$APP/traderock.ico" ]] \
  || { echo "publicação do Windows incompleta" >&2; exit 1; }
cp LICENSE THIRD-PARTY-NOTICES.md "$APP/"

echo "==> Python do Windows para o esptool"
fetch "$PY_WIN" "$PY_WIN_SHA256"
mkdir -p "$RUNTIME"
tar xzf "$CACHE/$PY_WIN" -C "$RUNTIME"
# Sem interface gráfica, testes, cabeçalhos nem pip (só roda o esptool).
rm -rf "$RUNTIME/python"/{tcl,include,libs,Scripts,Tools} \
       "$RUNTIME/python/Lib"/{test,idlelib,tkinter,turtledemo,ensurepip,lib2to3,pydoc_data} \
       "$RUNTIME/python/DLLs"/{_tkinter.pyd,tcl*.dll,tk*.dll} \
       "$RUNTIME/python/Lib/site-packages"/pip* "$RUNTIME/python"/pythonw*
# Símbolos de depuração (.pdb): mais da metade do tamanho.
find "$RUNTIME/python" -name '*.pdb' -delete
[[ -f "$RUNTIME/python/python.exe" ]] || { echo "python.exe ausente no runtime" >&2; exit 1; }

echo "==> esptool e dependências (hash fixo)"
# O pip daqui é o do Python do Linux (o do Windows não roda no Linux): ele instala as rodas
# win_amd64 numa pasta, sem executar nada delas.
fetch "$PY_LINUX" "$PY_LINUX_SHA256"
HOSTPY=$BUILD/hostpy
mkdir -p "$HOSTPY"
tar xzf "$CACHE/$PY_LINUX" -C "$HOSTPY"
PIP=("$HOSTPY/python/bin/python3" -m pip --disable-pip-version-check --no-input)
# O esptool só é publicado como código-fonte (puro Python): vira uma roda py3-none-any aqui.
grep -E '^esptool==' worker/requirements.lock > "$BUILD/esptool.lock"
"${PIP[@]}" wheel --quiet --no-deps --require-hashes --cache-dir "$CACHE/pip" -w "$BUILD/wheels" -r "$BUILD/esptool.lock"
SITE="$RUNTIME/python/Lib/site-packages"
"${PIP[@]}" install --quiet --no-deps --require-hashes --only-binary=:all: --cache-dir "$CACHE/pip" \
  --platform win_amd64 --python-version 3.12 --implementation cp --abi cp312 --abi abi3 --abi none \
  --target "$SITE" -r worker/requirements-esp32-windows.lock
rm -rf "$SITE/bin"
"${PIP[@]}" install --quiet --no-deps --no-index --only-binary=:all: \
  --platform win_amd64 --python-version 3.12 --implementation cp \
  --target "$SITE" "$BUILD"/wheels/esptool-*.whl
# Atalhos de linha de comando que o pip do Linux gera (scripts POSIX, inúteis no Windows).
rm -rf "$SITE/bin"
find "$SITE" -name __pycache__ -prune -exec rm -rf {} +

echo "==> worker (só a gravação do ESP32)"
mkdir -p "$RUNTIME/worker/traderock_worker"
cp worker/run.py "$RUNTIME/worker/"
cp worker/traderock_worker/{__init__.py,main.py,esp32_flash.py} "$RUNTIME/worker/traderock_worker/"
cat > "$RUNTIME/BUILD-INFO.json" <<JSON
{
  "python": "3.12.14+$PY_RELEASE (win x86_64)",
  "esptool": "$(sed -n 's/^esptool==\([^ ]*\).*/\1/p' worker/requirements.lock)"
}
JSON
rm -rf "$HOSTPY" "$BUILD/wheels" "$BUILD/esptool.lock"
echo "    $(du -sh "$APP" | cut -f1) em $APP"

if [[ $TESTS == 1 ]]; then
  echo "==> testes (win-x64)"
  dotnet publish app/TradeRock.Tests -c Release -r win-x64 --self-contained -o "$BUILD/tests" --nologo -v quiet
fi

if [[ $INSTALLER == 1 ]]; then
  echo "==> instalador (Inno Setup)"
  ISCC=${ISCC:-}
  if [[ -z $ISCC ]]; then
    for c in "/mnt/c/Program Files (x86)/Inno Setup 6/ISCC.exe" \
             "/mnt/c/Users/"*/AppData/Local/Programs/"Inno Setup 6"/ISCC.exe; do
      [[ -f $c ]] && { ISCC=$c; break; }
    done
  fi
  [[ -n $ISCC ]] || { echo "Inno Setup 6 (ISCC.exe) não encontrado; defina ISCC=..." >&2; exit 1; }
  winpath() { if command -v wslpath >/dev/null; then wslpath -w "$1"; else cygpath -w "$1" 2>/dev/null || echo "$1"; fi; }
  "$ISCC" /Q "/DAppVersion=$VERSION" "/DSourceDir=$(winpath "$PWD/$APP")" "/DOutputDir=$(winpath "$PWD/$DIST")" \
    "$(winpath "$PWD/packaging/windows/traderock.iss")"
  ls -la "$DIST/TradeRock-Setup-$VERSION.exe"
fi
