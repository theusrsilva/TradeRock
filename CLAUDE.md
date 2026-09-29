# TradeRock: instruções para o Claude Code

App desktop (.NET 10 + React) para trocar Pokémon com FireRed/LeafGreen do Switch via wireless local
(LDN), com público leigo. Especificação original completa: [docs/spec.md](docs/spec.md) (o projeto
se chamava "FRLG Trade Studio"). Decisões técnicas: [docs/decisions.md](docs/decisions.md) (D1–D16).

## Regras de trabalho (pedidas pelo autor, valem sempre)

- **Antes de cada entrega, apresente o plano e as dúvidas** e espere a resposta. Só então implemente.
- **Não invente APIs** do PKHeX, da biblioteca LDN (kinnay/LDN), do port em C# (easyworld), do
  firmware ESP32 ou do Photino. Consulte o código-fonte, que está nos submódulos de `engines/` ou no
  pacote NuGet. Se algo não existir, diga.
- Converse em **português**. Os textos da interface ficam em `frontend/src/locales/{pt-BR,en}.json`
  e os textos explicativos em `content/{pt-BR,en}/**/*.md`. Explique tudo de forma simples.
- **Commits:** cada entrega na própria branch (`entrega-N-nome`), criada a partir da anterior, com o
  trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Commite quando o autor pedir;
  o padrão combinado é commitar cada entrega ao terminá-la.
- Rode `scripts/ci.sh` antes de commitar. Ele roda typecheck, testes e build do frontend, os testes
  do worker (Python `unittest`) e o build e os testes do .NET.

## Segurança (inegociável)

- **prod.keys:** nunca exibir valores, logar, enviar pela rede nem incluir em backups, exportações ou
  relatórios. O arquivo fica com permissão 600. O app nunca baixa chaves nem sugere fontes ("só use
  as extraídas do seu próprio console").
- Nunca distribuir prod.keys nem ROMs.
- O app principal **nunca** roda como root e nunca pede nem guarda senha. O root fica isolado no
  worker (pkexec + polkit, D15).
- Sem telemetria. A verificação de atualizações é opcional e desligável (Entrega 8).

## Decisões do autor (resumo)

| Tema | Decisão |
| --- | --- |
| Runtime | .NET 10, não .NET 8 (o PKHeX.Core 26.8.26 só suporta net10.0) |
| Motores externos | Submódulos do upstream, sem fork. Ajustes via wrapper/patches nossos (`engines/patches`) |
| Placas sem teste | Entram no catálogo com o aviso "não testado" |
| Sprites | PokeAPI (distribuição só entre amigos por enquanto) |
| Ilustrações | SVG em vez de fotos |
| Trocas por sessão | Até 6 |
| Testes E2E | Só pela API |
| Construtor | Modo automático (AutoMod) por padrão e modo avançado. OT padrão = perfil "Meu treinador". Mensagens do PKHeX traduzidas só as da Gen 3. Nomes de golpes e itens em inglês |
| Cofre | Caixas de 30 posições com Caixa de entrada. Lixeira recuperável. Injeção em `.sav` sobrescreve, com backup. Exportação padrão: .pk3 de 100 bytes |
| Assistente | RTL8821CE também recomendada. Seletor de simulação só em desenvolvimento |
| Worker | Em Python (runtime embutido). Canal por stdin/stdout em vez de socket Unix. Polkit `auth_admin_keep`. Patches do FRLG-LDN-Trade-Console aplicados. `iw` e `ip` estáticos embutidos (o autor não quer mandar a pessoa instalar pré-requisitos) |
| Teste com o Switch | Até 30 s. Sessão de outro jogo decifrada não aprova. Vale até mudar a placa ou o prod.keys (não expira) |

## Estado das entregas

| # | Entrega | Estado |
| --- | --- | --- |
| 1 | Monorepo, servidor local, janela Photino | feita |
| 2 | FakeEngine + assistente de troca de ponta a ponta | feita |
| 3 | Construtor + legalidade (PKHeX + AutoMod) | feita |
| 4 | Cofre (caixas, tags, lixeira, exportação, injeção em .sav) | feita |
| 5 | Catálogo de hardware + assistente de primeira execução (7 etapas) | feita |
| 6 | Worker privilegiado + polkit + PythonLdnEngine + fluxo de teste no host | feita, **sem teste com hardware real** |
| 7 | Teste do beacon com a placa Wi-Fi (etapa 6 do assistente) | feita, **sem teste com hardware real** |
| 8 | Empacotamento Linux (AppImage e .deb) e CI | **próxima** |
| 9 | Esp32SerialEngine + gravação de firmware + validações ESP32 + beacon com ESP32 | — |
| 10 | Modo servidor na rede | — |
| 11 | Build e instalador Windows (só ESP32) | — |

A branch mais recente é `entrega-7-beacon`. O `main` do GitHub ainda tem só o commit inicial.

## Pendências já combinadas

- **Entrega 8:**
  - atualização do catálogo de hardware e do app pelo GitHub Releases, opcional e desligável; precisa
    da URL do repositório (`theusrsilva/TradeRock`);
  - `.deb` com `Depends: pkexec | policykit-1` e `procps`;
  - oferecer o código-fonte do `ip` embutido, que é GPL-2.0;
  - AppImage: copiar o worker para uma pasta do root na primeira execução, com confirmação, porque
    o `exec.path` do polkit não pode apontar para dentro do AppImage.

  Detalhes em D15 e em `scripts/build-worker.sh` / `install-worker.sh`.
- **Entrega 9:** passo do firmware do ESP32 em Configurações. Primeiro detectar se o firmware já está
  gravado (`LDN_HELLO` → `LDN_HELLO 1 <modelo> <caps> <mtu>`, ou `esptool verify-flash`); só depois
  oferecer gravar. O firmware do easyworld usa o ESP-IDF v6.1 fixo, no commit fff9895c. O GB-Link
  agora tem um host desktop em C# (Windows e Linux), que vale avaliar.
- **Nunca verificado de verdade:**
  - janela nativa e diálogos do Photino (o ambiente de desenvolvimento era WSL, sem WebKitGTK);
  - `.sav` real e o formato de save NSO do Switch;
  - troca com Switch real;
  - teste com o Switch (Entrega 7, D16);
  - teste da placa com root.

  O roteiro de teste no host está em [docs/dev-host.md](docs/dev-host.md).

## Mapa rápido do código

| Pasta | Conteúdo |
| --- | --- |
| `app/TradeRock.App` | ASP.NET Core + Photino (API em `Api/`, DI em `ServiceSetup.cs`) |
| `app/TradeRock.Core` | Domínio: `Trading/`, `Engines/` (Fake, PythonLdn, `Worker/`), `Pokemon/`, `Builder/`, `Storage/` (EF Core + SQLite), `Saves/`, `Setup/` (catálogo, probe do Linux, verificações, prod.keys) |
| `app/TradeRock.Tests` | xUnit v3; worker falso em `FakeWorker.cs` |
| `worker/` | Worker Python: `traderock_worker/` (server, radio, trade_child, card_child, beacon_child, events, protocol) e `tests/` (dublês de trio/ldn em `fake_ldn`) |
| `frontend/` | React + Vite + TS + Tailwind; o build vai para `app/TradeRock.App/wwwroot` |
| `catalog/hardware-catalog.json` | Catálogo de placas |
| `engines/` | Submódulos (frlg-ldn-trade, frlg-ldn-trade-esp32, PKHeX-Plugins) e `patches/` |
| `scripts/` | `ci.sh`, `dev-host.sh`, `build-worker.sh`, `build-nettools.sh`, `install-worker.sh` |

## Ambiente

- `git submodule update --init --recursive` antes de tudo.
- .NET 10 SDK, Node 24 e Python 3.12 (para os testes do worker).
- `scripts/build-worker.sh` baixa o Python embutido e o Zig. A primeira vez leva alguns minutos; o
  resultado fica em `~/.cache/traderock`.
- JSON dos enums: snake_case. xUnit v3 com Microsoft.Testing.Platform:
  `dotnet test --project app/TradeRock.Tests`.
