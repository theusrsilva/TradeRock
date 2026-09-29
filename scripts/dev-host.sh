#!/usr/bin/env bash
# Roda o TradeRock direto no host Linux (fora do container), com a janela nativa.
#
# Por que no host: o motor com a placa Wi-Fi (PythonLdnEngine) precisa de acesso direto ao
# phy da placa (/sys/class/ieee80211), de mexer no NetworkManager e de root (via worker + polkit).
# Nada disso funciona dentro de um container, nem no WSL.
#
# Hoje (Entrega 1) o script só compila o frontend, o app e o worker e abre a janela.
# A instalação da política polkit de desenvolvimento e o worker entram na Entrega 6.
set -euo pipefail
cd "$(dirname "$0")/.."

if grep -qi microsoft /proc/version 2>/dev/null; then
  echo "Aviso: isto é WSL. A janela abre (WSLg), mas o motor com a placa Wi-Fi não funciona aqui." >&2
fi

(cd frontend && npm ci --prefer-offline && npm run build)
dotnet build worker/TradeRock.Worker
DOTNET_ENVIRONMENT=Development dotnet run --project app/TradeRock.App -- --dev-tools "$@"
