#!/usr/bin/env bash
# Build + testes do TradeRock. Roda no CI dentro da imagem do .devcontainer e localmente.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "::group::Frontend"
(cd frontend && npm ci && npm run typecheck && npm test && npm run build)
echo "::endgroup::"

echo "::group::Worker (Python)"
python3 -m unittest discover -s worker/tests -t worker
echo "::endgroup::"

echo "::group::.NET build"
dotnet build TradeRock.slnx -c Release
echo "::endgroup::"

echo "::group::.NET testes"
dotnet test --solution TradeRock.slnx -c Release --no-build
echo "::endgroup::"
