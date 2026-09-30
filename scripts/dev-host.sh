#!/usr/bin/env bash
# Roda o TradeRock direto no host Linux (fora do container), com a janela nativa e o motor da
# placa Wi-Fi de verdade.
#
# Por que no host: o motor com a placa Wi-Fi (PythonLdnEngine) precisa de acesso direto ao phy da
# placa (/sys/class/ieee80211), de mexer no NetworkManager e de root. O root fica isolado no worker,
# iniciado via pkexec com uma política polkit própria. Nada disso funciona num container nem no WSL.
#
# O que o script faz:
#   1. compila o frontend e o app;
#   2. monta o worker (scripts/build-worker.sh: Python embutido + motor + patches);
#   3. se o worker instalado estiver ausente ou desatualizado, instala com sudo
#      (scripts/install-worker.sh dev → /usr/local/lib/traderock-dev + política polkit);
#   4. abre o app em modo desenvolvimento.
#
#   scripts/dev-host.sh                    tudo acima
#   scripts/dev-host.sh --no-worker        pula os passos 2 e 3
#   scripts/dev-host.sh --uninstall-worker remove o worker de desenvolvimento e a política
set -euo pipefail
cd "$(dirname "$0")/.."

PREFIX=/usr/local/lib/traderock-dev
worker=1
args=()
for a in "$@"; do
  case "$a" in
    --no-worker) worker=0 ;;
    --uninstall-worker) sudo scripts/install-worker.sh dev --uninstall; exit 0 ;;
    *) args+=("$a") ;;
  esac
done

if grep -qi microsoft /proc/version 2>/dev/null; then
  echo "Aviso: isto é WSL. A janela abre (WSLg), mas o motor com a placa Wi-Fi não funciona aqui." >&2
fi

(cd frontend && npm ci --prefer-offline && npm run build)
dotnet build app/TradeRock.App

if [[ $worker == 1 ]]; then
  scripts/build-worker.sh
  if ! diff -rq --exclude=__pycache__ build/worker/stage/worker "$PREFIX/worker" >/dev/null 2>&1 \
     || ! cmp -s build/worker/stage/BUILD-INFO.json "$PREFIX/BUILD-INFO.json" \
     || ! diff -rq --exclude=__pycache__ build/worker/stage/engine "$PREFIX/engine" >/dev/null 2>&1; then
    echo "==> instalando o worker de desenvolvimento em $PREFIX (pede a senha do sudo uma vez)"
    sudo scripts/install-worker.sh dev
  else
    echo "==> worker de desenvolvimento já está atualizado"
  fi
  for tool in pkexec sysctl; do  # iw e ip vêm embutidos no worker
    command -v "$tool" >/dev/null || echo "Aviso: '$tool' não encontrado. O assistente mostra como instalar." >&2
  done
fi

ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run --no-build --project app/TradeRock.App -- --dev-tools "${args[@]}"
