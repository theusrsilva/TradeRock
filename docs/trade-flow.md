# Fluxo de uma troca

## Peças

| Peça | Papel |
| --- | --- |
| `ITradeEngine` | Faz **uma tentativa** de conectar e trocar. Emite eventos. |
| `FakeEngine` | Simulação (modo demonstração), com cenários de falha. |
| `TradeCoordinator` | Uma sessão por vez: tentativas, timeouts, cancelamento, gravação e histórico. |
| `/api/trades/events` | Server-Sent Events. Quem conecta recebe a sessão atual desde o início (`Last-Event-ID` retoma). |

## Eventos e as mensagens do motor real

Os eventos de motor correspondem às mensagens de marco que o `frlg-ldn-trade` registra com
`lg.info(...)` / `self.info(...)`. Na Entrega 6, o script que embrulha o motor real traduz essas
linhas para os mesmos eventos.

| Evento | Mensagem do frlg-ldn-trade (arquivo) |
| --- | --- |
| `scanning` | "Scanning for the FRLG network..." (transport.py) |
| `beacon_found` / `beacon_decrypted` | beacon recebido pelo `ldn.scan`; decriptação falha gera o aviso "Failed to parse advertisement frame" no logger `ldn` |
| `join_requested` | "Joining the host..." (transport.py): **o jogador precisa aprovar no Switch** |
| `joined` | "Joined." / "Host accepted the link." (transport.py, sim.py) |
| `in_room` | "Host entered the trade room." (trade.py) |
| `host_sat` | "Host sat down." (trade.py) |
| `cards_exchanged` | "Exchanged trainer cards." (trade.py) |
| `trade_menu_open` | "Trade menu open; received the host's party." (frlgtrade.py) |
| `trade_offered` | "Offered our Pokémon; waiting on the host." (trade.py) |
| `confirming` | "Confirming trade..." (trade.py) |
| `trade_done` | "Trade confirmed." + .pk3 gravado no commit (frlgtrade.py) |
| `trade_cancelled` | "Trade cancelled (mutual)." / "Trade cancelled by one side..." (trade.py) |
| `leaving` | "All N trade(s) complete; cancelling to leave." / "Host is leaving the room..." |
| `link_closed` | "Link closed." (frlgtrade.py) |
| `error` | "Host rejected our join", "LDN join failed after N attempt(s)", etc. |

## Regras

- **Retry** (padrão 5 tentativas): só para falhas **antes** do evento `joined`. Depois de entrar
  na sala, repetir poderia deixar o jogo inconsistente, então a sessão termina.
- **Timeouts** (configuráveis): conexão (antes do pedido de entrada), aprovação no Switch, fase
  parada dentro da sala e sessão total. Eventos `log` não contam como progresso, porque o motor
  real registra "aguardando" a cada 2 segundos.
- **Recebidos** são gravados no cofre no instante do `trade_done`. Se a sessão falhar depois, o
  que já foi recebido fica guardado e o status vira `partially_completed`.
- **Cancelamento**: o coordenador cancela o motor e espera ele encerrar (e restaurar o rádio,
  nos motores reais) por até 30 s.
- **Fechar a janela durante uma troca**:
  1. O Photino segura o fechamento (`RegisterWindowClosingHandler` retorna `true`).
  2. O frontend pergunta ao usuário.
  3. Se confirmado, `POST /api/app/close` cancela com segurança e fecha a janela.

  No navegador (`--no-window`), o aviso é o `beforeunload`.
- **Evolução por troca**: o card avisa quando o Pokémon evolui ao ser trocado (tabela da
  pokefirered). O Switch pode travar ao salvar depois disso (issue #8 do frlg-ldn-trade).
