# Testar no host Linux (placa Wi-Fi real)

O motor com a placa Wi-Fi (`PythonLdnEngine`) **não roda em container nem no WSL**. Ele precisa de:

- **Acesso ao phy da placa.** O motor usa `/sys/class/ieee80211/phy*`, modo monitor e transmissão de
  quadros de ação via nl80211. Um container não enxerga o phy do host, e o WSL não tem placa Wi-Fi.
- **NetworkManager fora do caminho.** Ele não pode controlar as interfaces que a biblioteca LDN cria
  (`ldn*`).
- **root.** Criar interfaces e trocar de modo exige privilégio. No TradeRock isso fica isolado no
  worker, iniciado via `pkexec` com uma política polkit própria. O app principal nunca roda como
  root.

## Requisitos

- Linux instalado de verdade (dual boot serve).
- .NET 10 SDK e Node 24.
- `libwebkit2gtk-4.1-0` e `libnotify4`, para a janela.
- `iw`, `iproute2` e `pkexec`. No Ubuntu 24.04: `sudo apt install iw pkexec`.
- Um agente do polkit na sessão, que é o diálogo de senha. GNOME e KDE já trazem um.

## Rodar

```bash
scripts/dev-host.sh
```

O script:

1. compila o frontend e o app;
2. monta o worker com `scripts/build-worker.sh`:
   - o Python 3.12 do python-build-standalone, com o checksum conferido;
   - as dependências do motor, com hash fixo;
   - o frlg-ldn-trade com os patches de `engines/patches`;
3. instala o worker com `sudo`, se ele estiver ausente ou desatualizado:
   - em `/usr/local/lib/traderock-dev`, com dono root e sem escrita para outros usuários;
   - junto com a política `/usr/share/polkit-1/actions/io.github.traderock.worker.dev.policy`;
4. abre o app em modo desenvolvimento.

Opções:
- `--no-worker` pula os passos 2 e 3.
- `--uninstall-worker` remove o worker e a política.

## Roteiro de teste

1. **Configurações → Abrir o assistente.**
   - Na etapa 2, envie o `prod.keys`.
   - Na etapa 3, escolha a placa Wi-Fi.
2. **Etapa 4 → Testar a placa.**
   - O sistema pede a senha de administrador, uma vez por sessão do app.
   - Durante uns 10 s, o Wi-Fi da placa cai e volta sozinho.
   - Anote o resultado por canal (enviados e recebidos).
3. **Etapa 4 → Configurar o NetworkManager** (recomendado).
4. **Configurações → Motor de troca → Placa Wi-Fi do PC.** Concluir o assistente já seleciona esse
   motor.
5. **Trocar**, com o Switch no Direct Corner como Leader:
   - aprove o pedido de entrada ("EMU", ou o nome que você escolheu);
   - sente na cadeira da esquerda;
   - troque;
   - cancele no menu de troca;
   - saia da sala.
6. Se algo falhar:
   - **Resumo → Exportar relatório.**
   - Log de diagnóstico: rode com `--Logging:LogLevel:TradeRock=Debug` para ver as linhas `worker:`,
     que são o log verboso do motor. Nenhum desses logs tem chaves.

## Como as peças se encaixam

```
app (usuário) ──pkexec──▶ /usr/local/lib/traderock-dev/traderock-worker   (root)
     ▲   stdin/stdout JSON        │
     └────────────────────────────┤  python3 -I worker/run.py
                                  ├─ --child card   teste da placa (ldn.wlan)
                                  └─ --child trade  uma tentativa de troca (frlgtrade.run_live)
```

- **Uma operação de rádio por vez.** Cada troca e cada teste roda num processo filho. O motor instala
  o próprio tratador de Ctrl+C, que só funciona na thread principal.
- **Cancelar:**
  1. primeiro SIGINT: o motor sai da sala e espera o Switch fechar (até 30 s);
  2. segundo SIGINT: encerra na hora (até 10 s);
  3. SIGKILL.

  Em todos os casos, o worker apaga as interfaces `ldn*` e devolve cada interface da placa ao estado
  anterior (ligada e gerenciada pelo NetworkManager, se era assim).
- **App fechado ou travado:** o stdin do worker fecha, e ele cancela, restaura o rádio e sai.
- **Arquivos:** `.pk3` e `prod.keys` chegam como bytes e ficam numa pasta temporária do root
  (permissão 700), apagada no fim. O Pokémon recebido volta como bytes no instante do commit.

## Testes sem hardware

- `python3 -m unittest discover -s worker/tests -t worker` cobre:
  - validação, rádio (com `/sys` falso) e mapeamento de eventos;
  - o wrapper contra um motor falso e o laço de protocolo;
  - um teste que confere se cada frase do motor usada no mapeamento ainda existe no submódulo.
- `WorkerTests.cs` cobre a API de ponta a ponta com um worker falso: assistente, teste da placa,
  troca, cancelamento e retry.
