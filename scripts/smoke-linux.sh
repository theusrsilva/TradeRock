#!/usr/bin/env bash
# Teste de fumaça dos pacotes Linux (roda no CI e à mão, como root, numa máquina ou container limpo):
#
#   scripts/smoke-linux.sh deb dist/traderock_*.deb
#       instala com apt (com as dependências), confere a versão, roda o app como usuário comum,
#       confere que ele acha o worker do pacote (seguro, com iw/ip embutidos), fala com o worker
#       como root e remove o pacote (os dados do usuário ficam).
#
#   scripts/smoke-linux.sh appimage dist/TradeRock-*.AppImage
#       roda o AppImage (extrair e rodar, sem FUSE), instala o worker embutido com o mesmo script que
#       o app usa (packaging/linux/worker-setup.sh), confere que o app passa a usá-lo e remove.
#
# Não mexe na placa Wi-Fi: o worker só responde ao "hello".
set -euo pipefail
cd "$(dirname "$0")/.."

KIND=${1:?deb|appimage}
PKG=$(realpath "${2:?arquivo do pacote}")
USER_NAME=traderock-smoke
[[ $EUID -eq 0 ]] || { echo "Rode como root (sudo)." >&2; exit 1; }

fail() { echo "FALHOU: $*" >&2; exit 1; }
id "$USER_NAME" >/dev/null 2>&1 || useradd -m "$USER_NAME"
HOME_DIR=$(getent passwd "$USER_NAME" | cut -d: -f6)

# Sobe o app como usuário comum (--no-window) e devolve "base token".
start_app() { # comando
  local log="$HOME_DIR/app.log"
  rm -f "$log"
  su "$USER_NAME" -s /bin/sh -c "cd ~ && nohup $1 --no-window > '$log' 2>&1 &"
  for _ in $(seq 60); do
    if grep -q "http://127.0.0.1" "$log" 2>/dev/null; then break; fi
    sleep 1
  done
  local url
  url=$(grep -o 'http://127.0.0.1:[0-9]*/?t=[A-Za-z0-9_-]*' "$log" | head -1) || { cat "$log" >&2; fail "o app não subiu"; }
  echo "${url%%/?t=*} ${url##*t=}"
}

api() { # base token caminho
  curl -fsS -H "X-TradeRock-Token: $2" "$1$3"
}

stop_app() {
  pkill -u "$USER_NAME" -f traderock || true
  sleep 1
}

json() { # expressão python sobre "d"
  python3 -c "import json,sys; d=json.load(sys.stdin); print($1)"
}

hello() { # launcher
  local out
  out=$(echo '{"id":1,"op":"hello"}' | "$1" | head -1)
  echo "$out" | json 'd["ok"] and d["result"]["root"] and not d["result"]["missingTools"]' | grep -qx True \
    || fail "o worker não respondeu direito: $out"
  echo "    worker: $(echo "$out" | json 'd["result"]["version"], d["result"]["tools"]["iw"]')"
}

case "$KIND" in
  deb)
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq "$PKG" curl python3 >/dev/null
    echo "==> $(traderock --version)"
    [[ $(stat -c %U /usr/lib/traderock/traderock-worker) == root ]] || fail "o worker não é do root"
    [[ -z $(find /usr/lib/traderock -perm /022 ! -type l) ]] || fail "arquivos graváveis por outros usuários"
    hello /usr/lib/traderock/traderock-worker
    read -r base token < <(start_app traderock)
    api "$base" "$token" /api/health | json 'd["version"]'
    worker=$(api "$base" "$token" /api/worker)
    echo "    $worker"
    echo "$worker" | json 'd["install"]["flavor"] == "release" and d["install"]["safe"] and d["install"]["bundledTools"] and not d["bundled"]["available"]' \
      | grep -qx True || fail "o app não achou o worker do pacote"
    api "$base" "$token" /sobre | grep -q '<div id="root">' || fail "o frontend não foi servido"
    stop_app
    apt-get remove -y -qq traderock >/dev/null
    [[ ! -e /usr/lib/traderock && ! -e /usr/share/polkit-1/actions/io.github.traderock.worker.policy ]] || fail "sobrou algo do pacote"
    [[ -f "$HOME_DIR/.local/share/traderock/traderock.db" ]] || fail "os dados do usuário sumiram"
    ;;
  appimage)
    WORK="$HOME_DIR/appimage"
    rm -rf "$WORK" && mkdir -p "$WORK" && cp "$PKG" "$WORK/TradeRock.AppImage" && chown -R "$USER_NAME" "$WORK"
    version=$(su "$USER_NAME" -s /bin/sh -c "cd '$WORK' && TMPDIR='$WORK' APPIMAGE_EXTRACT_AND_RUN=1 ./TradeRock.AppImage --version")
    [[ $version == "TradeRock "* ]] || fail "o AppImage não rodou: $version"
    echo "==> $version"
    su "$USER_NAME" -s /bin/sh -c "cd '$WORK' && ./TradeRock.AppImage --appimage-extract >/dev/null"
    APP="$WORK/squashfs-root"
    PAYLOAD="$APP/usr/lib/traderock/worker-payload"
    PREFIX=/usr/local/lib/traderock
    POLICY=/usr/share/polkit-1/actions/io.github.traderock.worker.appimage.policy

    read -r base token < <(start_app "$APP/AppRun")
    api "$base" "$token" /api/worker | json 'd["bundled"]' | grep -q "'available': True, 'installed': False" || fail "o worker embutido não foi visto"
    # O mesmo que o app faz depois da senha: pkexec /bin/sh -c <script> install ... < worker.tar
    tar -C "$PAYLOAD" -cf - . | sh -c "$(cat packaging/linux/worker-setup.sh)" worker-setup install "$PREFIX" "$POLICY"
    [[ $(stat -c %U "$PREFIX/traderock-worker") == root ]] || fail "o worker instalado não é do root"
    [[ -z $(find "$PREFIX" -perm /022 ! -type l) ]] || fail "arquivos graváveis por outros usuários"
    grep -q "exec.path\">$PREFIX/traderock-worker<" "$POLICY" || fail "a política não aponta para o worker instalado"
    hello "$PREFIX/traderock-worker"
    worker=$(api "$base" "$token" /api/worker)
    echo "    $worker"
    echo "$worker" | json 'd["install"]["flavor"] == "appimage" and d["install"]["safe"] and d["bundled"]["upToDate"]' \
      | grep -qx True || fail "o app não passou a usar o worker instalado"
    sh -c "$(cat packaging/linux/worker-setup.sh)" worker-setup uninstall "$PREFIX" "$POLICY"
    [[ ! -e $PREFIX && ! -e $POLICY ]] || fail "sobrou algo do worker"
    stop_app
    ;;
  *) echo "uso: $0 deb|appimage <pacote>" >&2; exit 2 ;;
esac
echo "==> ok ($KIND)"
