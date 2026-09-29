# Testar no host Linux (placa Wi-Fi real)

O motor com a placa Wi-Fi (`PythonLdnEngine`) **não roda em container nem no WSL**. Ele precisa de:

- **Acesso ao phy da placa:** `/sys/class/ieee80211/phy*`, modo monitor e transmissão de action
  frames via nl80211. Um container não enxerga o phy do host, e o WSL não tem placa Wi-Fi.
- **NetworkManager:** as interfaces criadas pela biblioteca LDN precisam ficar fora do controle
  dele.
- **root:** criar interfaces e trocar de modo exige privilégio. No TradeRock isso fica isolado no
  `traderock-worker`, iniciado via `pkexec` com uma política polkit própria. O app principal nunca
  roda como root.

Por isso existe o `scripts/dev-host.sh`, que compila e roda tudo direto no host.

| Entrega | O script faz |
| --- | --- |
| 1 (agora) | Compila o frontend, o app e o worker e abre a janela. |
| 6 | Também instala a política polkit de desenvolvimento e sobe o worker com a placa real. |

Requisitos no host: Linux instalado de verdade (dual boot serve), .NET 10 SDK, Node 24,
`libwebkit2gtk-4.1-0` e `libnotify4`.
