#!/usr/bin/env bash
# Instala (como root) o worker montado por scripts/build-worker.sh e a política polkit dele.
#
#   sudo scripts/install-worker.sh dev        → /usr/local/lib/traderock-dev  (io.github.traderock.worker.dev)
#   sudo scripts/install-worker.sh release    → /usr/lib/traderock            (io.github.traderock.worker)
#   sudo scripts/install-worker.sh dev --uninstall
#
# Tudo fica com dono root e sem escrita para outros usuários: o polkit deixa o app rodar só este
# launcher como root, então nenhum arquivo que ele executa pode ser alterável sem ser root.
set -euo pipefail
cd "$(dirname "$0")/.."

case "${1:-}" in
  dev)     PREFIX=/usr/local/lib/traderock-dev; ACTION=io.github.traderock.worker.dev ;;
  release) PREFIX=/usr/lib/traderock;           ACTION=io.github.traderock.worker ;;
  *) echo "uso: $0 dev|release [--uninstall]" >&2; exit 2 ;;
esac
POLICY=/usr/share/polkit-1/actions/$ACTION.policy

if [[ $EUID -ne 0 ]]; then
  echo "Rode com sudo." >&2; exit 1
fi

if [[ "${2:-}" == "--uninstall" ]]; then
  rm -rf "$PREFIX" "$POLICY"
  echo "Worker removido ($PREFIX, $POLICY)."
  exit 0
fi

STAGE=build/worker/stage
[[ -f "$STAGE/BUILD-INFO.json" ]] || { echo "Rode scripts/build-worker.sh antes." >&2; exit 1; }

tmp="$PREFIX.new"
rm -rf "$tmp"
mkdir -p "$tmp"
cp -a "$STAGE/." "$tmp/"
sed -e "s|@PREFIX@|$PREFIX|g" -e "s|@ACTION_ID@|$ACTION|g" worker/traderock-worker.in > "$tmp/traderock-worker"
chown -R root:root "$tmp"
chmod -R u+rwX,go+rX,go-w "$tmp"
chmod 755 "$tmp/traderock-worker"
rm -rf "$PREFIX"
mv "$tmp" "$PREFIX"

sed -e "s|@PREFIX@|$PREFIX|g" -e "s|@ACTION_ID@|$ACTION|g" worker/polkit.policy.in > "$POLICY"
chown root:root "$POLICY"
chmod 644 "$POLICY"
echo "Worker instalado em $PREFIX (política $ACTION)."
