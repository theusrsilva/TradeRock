# TradeRock

Troque Pokémon com **Pokémon FireRed e LeafGreen do Nintendo Switch / Switch 2** usando o
wireless local do jogo (LDN). O seu PC, ou uma plaquinha ESP32, finge ser um segundo Switch na
sala de troca do Direct Corner.

> **Estado:** em desenvolvimento (Entrega 4 de 11). Já dá para **criar Pokémon legais** no app,
> organizá-los no **cofre** (caixas, tags, busca, exportação, injeção em `.sav` de GBA) e fazer o
> assistente de troca de ponta a ponta em **modo demonstração** (motor simulado). Ainda não troca
> com um Switch de verdade.

O TradeRock nunca baixa, sugere fontes nem distribui `prod.keys` ou ROMs. Use só as chaves
extraídas do seu próprio console.

## Plataformas

| Sistema | Motor de troca | Situação |
| --- | --- | --- |
| Linux x64 (AppImage, .deb) | Placa Wi-Fi do PC (padrão) ou ESP32 via USB | Fase 1 |
| Windows x64 (instalador) | Só ESP32 via USB | Fase 2 |

No Windows, a placa Wi-Fi do PC não serve: o sistema não dá o acesso de baixo nível ao Wi-Fi
que o LDN exige.

## Arquitetura

```mermaid
flowchart LR
    subgraph proc["traderock (processo .NET 10, usuário comum)"]
        win["Janela Photino<br/>(WebKitGTK / WebView2)"]
        api["ASP.NET Core<br/>127.0.0.1:porta aleatória<br/>API + frontend embutido"]
        eng{{"ITradeEngine"}}
        win -- "cookie de sessão" --> api
        api --> eng
    end
    eng --> fake["FakeEngine<br/>(simulação, dev)"]
    eng --> py["PythonLdnEngine<br/>(Linux)"]
    eng --> esp["Esp32SerialEngine<br/>(Linux e Windows)"]
    py -- "socket Unix" --> worker["traderock-worker<br/>(root via pkexec + polkit)"]
    worker --> wifi["frlg-ldn-trade<br/>+ placa Wi-Fi"]
    esp -- "USB serial" --> board["ESP32 + firmware"]
    wifi -. "LDN" .-> sw["Switch<br/>FireRed/LeafGreen"]
    board -. "LDN" .-> sw
```

- **Segurança local:** o servidor só escuta em `127.0.0.1`, recusa cabeçalhos `Host` desconhecidos
  (proteção contra DNS rebinding) e exige um token de sessão aleatório. A janela recebe o token na
  URL inicial, que o servidor troca por um cookie `HttpOnly; SameSite=Strict`. O app principal
  nunca roda como root.
- **Motores externos** entram como submódulos git em `engines/`, sem copiar código:
  - [`tornadus/frlg-ldn-trade`](https://github.com/tornadus/frlg-ldn-trade): motor Python (placa Wi-Fi).
  - [`easyworld/frlg-ldn-trade-esp32`](https://github.com/easyworld/frlg-ldn-trade-esp32): core C# e firmware ESP32.
  - [`santacrab2/PKHeX-Plugins`](https://github.com/santacrab2/PKHeX-Plugins): PKHeX.Core.AutoMod.

## Estrutura

```
app/TradeRock.App/       executável: servidor + janela
app/TradeRock.Core/      domínio: troca (coordenador, motores), Pokémon (PKHeX), armazenamento (SQLite)
app/TradeRock.Tests/     testes (xUnit v3)
worker/TradeRock.Worker/ worker privilegiado (Linux)
frontend/                React + Vite + TypeScript + Tailwind
engines/                 submódulos (motores e AutoMod)
firmware/                firmware ESP32 embutido (vem do submódulo do easyworld)
content/                 textos explicativos em markdown, por idioma
packaging/               AppImage, .deb, polkit, instalador Windows
docs/                    decisões e guias de desenvolvimento
```

## Desenvolvimento

Clone com os submódulos:

```bash
git clone --recurse-submodules <url>
# ou, num clone existente:
git submodule update --init
```

### Opção 1: Dev Container (VS Code)

Abra a pasta no VS Code e escolha **Reopen in Container**. A imagem (`.devcontainer/Dockerfile`)
traz .NET 10, Node LTS, Python 3.12, esptool e as dependências de empacotamento. É a mesma
imagem usada no CI.

### Opção 2: docker compose (hot reload)

```bash
docker compose -f docker-compose.dev.yml up --build
```

Abra <http://localhost:5173>. O backend roda com `dotnet watch` e o Vite repassa `/api` para ele
com um token fixo de desenvolvimento. Só a porta do Vite é publicada, e só em `127.0.0.1`.

### Opção 3: direto no host (Linux)

Precisa do .NET 10 SDK, do Node 24 e das bibliotecas da janela:

```bash
sudo apt install libwebkit2gtk-4.1-0 libnotify4   # Debian/Ubuntu
scripts/dev-host.sh
```

Para o motor com a placa Wi-Fi real, este é o **único** caminho. Veja [docs/dev-host.md](docs/dev-host.md).

### Comandos úteis

```bash
scripts/ci.sh                                  # build + todos os testes (igual ao CI)
dotnet run --project app/TradeRock.App -- --no-window   # só servidor; imprime a URL com o token
cd frontend && npm run dev                     # Vite; backend em --no-window --port 5080 (perfil do launchSettings)
```

Opções do executável:

| Opção | Efeito |
| --- | --- |
| `--no-window` | Só o servidor. A URL de acesso (com o token) sai no terminal. |
| `--port N` | Porta fixa (padrão: aleatória). |
| `--data-dir DIR` | Pasta de dados alternativa. |
| `--dev-tools` | Ferramentas de desenvolvedor e menu de contexto na janela. |
| `--bind ADDR` | Só no ambiente `Development` (containers). Fora dele, apenas loopback. |

### ESP32 dentro do container

Repasse a porta serial para o container:

- **Dev Container:** descomente `runArgs` em `.devcontainer/devcontainer.json`, por exemplo
  `"--device=/dev/ttyUSB0"` (ou `/dev/ttyACM0` em placas com USB nativo).
- **docker compose:** descomente `devices` no serviço `backend`.

O usuário `dev` do container já está no grupo `dialout`. No host, confira qual porta apareceu
com `ls /dev/ttyUSB* /dev/ttyACM*` depois de conectar a placa.

### Construtor de Pokémon

Em **Criar Pokémon**, escolha a espécie e o que quiser dela (nível, natureza, habilidade, gênero,
shiny, IVs, EVs, golpes, item, Poké Bola, idioma, origem, treinador original). O TradeRock usa o
**PKHeX.Core.AutoMod** para gerar uma versão legal e o **LegalityAnalysis do PKHeX** para conferir,
a cada mudança. Quando algo não é possível, a tela mostra o que mudou ou o que impede.

- **Modo avançado:** edição dos campos brutos (PID, IVs, local de encontro…), como no PKHeX.
- **Importar:** texto do Showdown, `.pk3`/`.ek3` e `.sav` de GBA (time e caixas).
- **Meu treinador** (Configurações): dados do seu save, usados como OT padrão e nos avisos de
  obediência e de Pokédex Nacional.
- Pokémon ilegal não vai para o Switch, a não ser no modo avançado de troca, com confirmação.

### Cofre

- **Caixas de 30 posições**, como no jogo. Pokémon novos chegam na **Caixa de entrada**.
- Arraste para mudar de posição ou de caixa. Soltar numa posição ocupada troca os dois de lugar.
- **Busca** (espécie, número, apelido, treinador) e **filtros**: legalidade, origem, nível, shiny,
  avisos, possíveis duplicatas e tags.
- **Tags** com cor, também aplicadas em lote.
- **Lixeira** recuperável. Esvaziar a lixeira mantém os Pokémon que estão no histórico de trocas.
- **Exportar** `.pk3`/`.ek3`, com 100 ou 80 bytes. Na janela do app, o sistema pergunta onde salvar.
  No navegador, o arquivo é baixado (vários de uma vez viram `.zip`).
- **Injetar num `.sav`** de GBA:
  - só em posições vazias;
  - a Pokédex do save é atualizada;
  - um backup do original vai para `backups/` na pasta de dados;
  - o resultado é relido e conferido pelo PKHeX.

  Na janela do app, o arquivo é alterado no mesmo lugar. No navegador, você baixa o save novo.
- Solte `.pk3`/`.ek3`/`.sav` em qualquer lugar da janela para importar.

### Modo demonstração

Sem Switch, placa ou prod.keys, o TradeRock usa o **FakeEngine**, que simula a troca inteira no
ritmo real. Na etapa "Jogo e treinador" dá para escolher o que o Switch simulado faz: dar certo,
falhar algumas vezes antes de conectar, chaves erradas, cancelar a troca etc. Para acelerar a
simulação em desenvolvimento, use `--Engines:Fake:SpeedFactor=0.2` (0 = instantâneo).

Na primeira execução, o app já traz dois Pokémon de demonstração (MEWTWO e DEOXYS, do repositório
do easyworld). Detalhes do fluxo em [docs/trade-flow.md](docs/trade-flow.md).

## Pastas de dados

| Sistema | Pasta |
| --- | --- |
| Linux | `$XDG_DATA_HOME/traderock` (padrão `~/.local/share/traderock`) |
| Windows | `%APPDATA%\TradeRock` |

Dentro dela:

| Pasta ou arquivo | Conteúdo |
| --- | --- |
| `traderock.db` | Banco SQLite |
| `vault/` | Arquivos `.pk3`, nomeados pelo SHA-256 |
| `backups/` | Cópias dos saves antes de qualquer injeção |
| `keys/` | `prod.keys`, com permissão 700 |
| `logs/` | Logs do app |

Migrações do banco: `dotnet tool restore && dotnet ef migrations add <Nome> --project app/TradeRock.Core --output-dir Storage/Migrations`.

## Solução de problemas

**"Não foi possível abrir a janela do TradeRock: faltam bibliotecas do sistema"**
Instale o WebKitGTK 4.1 e o libnotify (o comando aparece na mensagem) ou rode com `--no-window`.

A seção de placas Wi-Fi e ESP32 chega com o catálogo de hardware (Entrega 5).

## Licença e créditos

TradeRock é software livre sob a **GNU AGPL-3.0**. Veja [LICENSE](LICENSE) e
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
