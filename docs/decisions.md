# Registro de decisões

Decisões tomadas com base na leitura do código-fonte dos projetos de origem (2026-09-29).

## D1. .NET 10 no lugar do .NET 8

A especificação original pedia .NET 8. Não é viável:

- PKHeX.Core 26.8.26 (a versão atual) só publica `net10.0`.
- PKHeX.Core.AutoMod (tag 26.08.26) e o `host/core` do easyworld exigem `net10.0`.

O .NET 10 é LTS (suporte até nov/2028). Com .NET 8 teríamos que congelar o PKHeX numa versão antiga.

## D2. Submódulos apontando para o upstream, sem fork

- `engines/frlg-ldn-trade` → tornadus, commit `13809c2`.
- `engines/frlg-ldn-trade-esp32` → easyworld, commit `55a4753`.
- `engines/PKHeX-Plugins` → santacrab2, tag `26.08.26`.

Os eventos estruturados (`--json-events`) vão ser gerados por um script **nosso**, fora do
submódulo. O `frlgtrade.py` envia toda a saída por uma função de log (`lg`), então o script pode
importar o `frlgsim` e trocar essa função. Correções pontuais, se necessárias (ex.: PR #9 do
tornadus), entram como arquivos `.patch` versionados aqui e aplicados no build.

O fork do MercuryEnigma **não** é usado como base. As opções de treinador dele
(`--ot`, `--version`, `--id`, Pokédex Nacional) já estão no upstream, e o que ele acrescenta
(o PC como Leader) não faz parte do nosso fluxo.

## D3. PKHeX.Core.AutoMod não está no NuGet

A fonte é o repositório `santacrab2/PKHeX-Plugins` (MIT). Referenciamos só o projeto
`PKHeX.Core.AutoMod.csproj` do submódulo, na mesma versão do PKHeX.Core.

## D4. Chaves exigidas do prod.keys

Conferido em `kinnay/LDN` (`KeyDerivation`) e no `KeyFile.cs` do easyworld:

- obrigatórias: `aes_kek_generation_source`, `aes_key_generation_source`;
- e pelo menos uma: `master_key_00` (protocolo LDN 1) ou `master_key_12` (protocolo LDN 3).

## D5. Teste do beacon (Etapa 6 do assistente)

`ldn.scan` descarta beacons que não decriptam e registra `"Failed to parse advertisement frame,
ignoring it"` no logger `ldn`. Capturando esse aviso, dá para distinguir "nenhum beacon" de
"beacon encontrado mas não decriptado" (chaves erradas).

## D6. Informações de hardware que divergem da especificação

Para o catálogo (Entrega 5):

- **RTL8822CE:** não encontrado em nenhuma fonte (README, issues, PRs, wiki). Entra como
  "não testado".
- **ESP32-C3:** o README do easyworld relata entrada na sala e uma troca completa.
- **ESP32-S3:** o autor do PR completou uma troca; o firmware publicado em 09/09/2026 não foi
  retestado em hardware real.
- **ESP32-C6:** duas trocas reais relatadas.
- **Placas novas nas fontes:** COMFAST MT7612U funciona (issue #5); Intel AX211 sem modo monitor
  (comentário na issue #5); RT5370 com `rt2800usb` e `nohwcrypt=1` (PR #9, ainda aberto).
- **GB-Link:** recomenda ESP32 clássico e S3, mas usa outro firmware e outro protocolo. Não se
  aplica ao motor do easyworld.

## D7. NetworkManager

O PR #9 do tornadus mostra que marcar interfaces como "unmanaged" pelo nome não basta: o udev
renomeia `ldnclient` para `wlx<mac>`. A configuração vai ser revista na Entrega 6.

## D8. Evolução por troca trava o Switch

Issue #8 do tornadus: mandar um Pokémon que evolui por troca (ex.: Kadabra) trava o jogo ao
salvar. O construtor e o assistente de troca devem avisar.

## D9. Nome do produto

O produto se chama **TradeRock**: executável `traderock`, worker `traderock-worker`, pasta de
dados `~/.local/share/traderock` e `%APPDATA%\TradeRock`.

## D10. Logs e o token de sessão

- A URL inicial carrega o token (`?t=`). Por isso o log `Microsoft.AspNetCore.Hosting.Diagnostics`
  fica restrito a avisos.
- O Photino roda com `LogVerbosity = 0`, porque senão imprime a URL no stdout.
- Com `--no-window`, a URL sai só no stdout, de propósito.

## D11. Troca (Entrega 2)

- **PKHeX.Core** entra já na Entrega 2, só para ler e exibir `.pk3`/`.ek3`. O `new PK3(bytes)`
  decripta sozinho (`PokeCrypto.DecryptIfEncrypted3`); os bytes saem por
  `WriteDecryptedDataParty/Stored`. O PKHeX não tem nomes de espécie em português; usamos os em
  inglês, que são os mesmos no Brasil.
- **Cofre mínimo**: tabela `Pokemon` + arquivos por SHA-256. O hash é dos bytes **decriptados**, então
  o `.pk3` e o `.ek3` do mesmo Pokémon são um só. A Entrega 4 amplia.
- **Nome do treinador**: no máximo 7 caracteres da tabela Gen III de `frlgsim/charmap.py`
  (sem acentos). O nome é o que aparece no pedido de entrada, então as instruções usam o nome
  escolhido, não "EMU".
- **Time**: de 2 a 6 Pokémon e de 1 a 6 trocas (`--trades`/`--slots` do motor). O mínimo de 2 existe
  porque o motor recusa trocar o último Pokémon vivo.
- **Sprites** do PokeAPI (via easyworld): aceitos pelo dono do projeto para distribuição entre
  amigos. Rever antes de uma distribuição pública.
- **Enums na API** em `snake_case` (`join_requested`, `fire_red`).

## D12. Construtor e legalidade (Entrega 3)

**AutoMod** (`app/TradeRock.Core/Builder/AutoModEnvironment.cs`). As opções dele são estáticas e
globais no processo, então as gerações rodam uma por vez. Opções que precisaram mudar:

- `Legalizer.EnableEasterEggs = false`: o padrão (`true`) troca o Pokémon por outra espécie "de meme"
  quando a geração falha.
- `APILegality.EnableDevMode = true`: com ele desligado, o AutoMod consulta na internet a última
  versão do PKHeX (`ALMVersion` → `UpdateUtil.GetLatestPKHeXVersion`). O app não faz requisições sozinho.
- `AllowTrainerOverride = true`, `UseTrainerData = false`: sem isso, o AutoMod ignora o treinador
  pedido e usa o do banco dele ("PKHeX", 12345/54321). O OT vai nas linhas `OT/TID/SID/OTGender`.
- `ForceLevel100for50 = false`: o padrão troca nível 50 por 100 (times de VGC).
- `SetMatchingBalls = false`: o padrão escolhe a Poké Bola "da cor" da espécie.

**Ordem da origem.** O gerador de encontros do PKHeX lista primeiro os eventos (o Ho-Oh "MATTLE" do
Colosseum vinha antes da Navel Rock). O TradeRock ordena os candidatos assim:

1. Jogo do Switch escolhido.
2. O outro FRLG.
3. Emerald, depois Ruby/Sapphire.
4. Colosseum/XD.

Dentro de cada jogo, a preferência é estático/selvagem/troca/presente, depois ovo. Eventos ficam por
último. O construtor tenta até 4 candidatos e depois o AutoMod livre, e fica com o resultado legal
mais fiel ao pedido. Presentes no local 255 ("fateful encounter") contam como evento.

**Apelido** vai pelo texto (`Apelido (Espécie)`), porque o AutoMod grava os bytes residuais do nome
como o jogo. Gravar o apelido depois gera `TrashBytesMismatchInitial`. O **item** entra depois da
geração (ID interno da Gen 3).

**Falhas.** Quando nada dá certo, o construtor afrouxa um pedido por vez (nível mínimo, shiny,
natureza, IVs, golpes, Poké Bola, habilidade, gênero, origem) e diz o que impede. O Pokémon
"vazio" que o AutoMod devolve na falha não é mostrado nem pode ser salvo.

**Mensagens.** O PKHeX não tem português. As 149 mensagens relevantes para a Gen 3, mais as de
golpes, encontro e gerais, estão em `content/pt-BR/pkhex/*.json` e entram por cima do inglês.
O resto fica em inglês. Os nomes de espécies, golpes e itens ficam em inglês (decisão do projeto).

**"Não verificável".** A versão de Switch (27/02/2026) não teve distribuições. Depois do Hall da
Fama, o jogo dá os tickets Mystic (Navel Rock: Lugia, Ho-Oh) e Aurora (Birth Island: Deoxys), e
esses encontros o PKHeX 26.8.26 já conhece (`Encounters3FRLG.cs`). A regra existe, mas a lista de
espécies (`Builder/Data/unverifiable.json`) começa vazia.

**Regras do jogo**, conferidas na pokefirered:

- **Pokédex Nacional.** `trade.c` `CanTradeSelectedMon`: sem a Nacional, não há troca de #152+ nem
  de ovos.
- **Mew e Deoxys** precisam da flag de encontro fatídico.
- **Obediência.** `battle_util.c` `IsMonDisobedient`: limites 10, 30, 50 e 70 conforme as
  insígnias; com a 8ª, obedece sempre. `IsOtherTrainer` compara o ID de 32 bits e o nome.
- **Evolução por troca**, já tratada desde a Entrega 2.

**`.sav`.** A leitura do arquivo é do PKHeX (`SaveUtil.GetSaveFile`). O PKHeX não grava um save em
branco que ele mesmo reconheça depois, então o teste cobre a extração a partir de um `SAV3FRLG`
em memória. **Falta testar com um `.sav` real.**

## D13. Cofre e saves (Entrega 4)

- **Caixas de 30 posições** e Caixa de entrada (`BoxId` null). Um índice único em `(BoxId, Slot)`
  garante uma posição por Pokémon. Mover para uma posição ocupada troca os dois de lugar, numa
  transação.
- **Lixeira** com `DeletedAt`. Esvaziar não apaga Pokémon referenciados no histórico de trocas.
  Importar de novo um arquivo que está na lixeira restaura esse Pokémon.
- **Possível duplicata**: mesma espécie, PID e treinador (TID, SID e nome), com hash diferente.
  Arquivos idênticos já não se repetem.
- **Downloads na janela nativa.** O WebView (WebKitGTK/WebView2) do Photino não é confiável com
  `<a download>`. Na janela, exportar e injetar usam os diálogos nativos do Photino
  (`ShowSaveFileAsync`, `ShowOpenFileAsync`, `ShowOpenFolderAsync`, que já despacham para a thread
  da interface), e o backend grava o arquivo. No navegador, download normal.
- **Injeção.**
  - Só em posições vazias.
  - `EntityImportSettings(UpdateToSaveFile: Disable, UpdatePokeDex: Enable, UpdateRecord: Disable)`:
    o Pokémon não muda e a Pokédex marca capturado.
  - O resultado é relido pelo PKHeX e conferido (espécie, PID, checksum).
  - Com a janela, se o arquivo mudou no disco desde que foi aberto (o emulador salvou), a injeção é
    recusada.
  - O PKHeX preserva o rodapé de RTC dos emuladores e o cabeçalho NSO ("SRAM", re-assinado com
    SHA-1). Não foi testado com o save da versão de Switch de 2026, porque não temos um de exemplo.
- **Save de teste** (`app/TradeRock.Tests/SaveFixture.cs`). O save em branco do PKHeX não tem os
  rodapés dos setores e não é reconhecido depois de gravado. O fixture monta:
  - os IDs 0..13 e a assinatura `0x08012025` nos 14 setores;
  - `1` em 0xAC, a marca de FRLG;
  - `0xFF` nos bytes 6 e 7, a marca de jogo internacional.

  Com isso, leitura e injeção são testadas de ponta a ponta com um `.sav` real de 128 KB, sem
  arquivo baixado da internet.

## D14. Assistente de configuração e catálogo (Entrega 5)

- **Catálogo** em `catalog/hardware-catalog.json`, embutido no `TradeRock.Core`. Cada placa tem
  status (`recommended`, `works`, `untested`, `unstable`, `problematic`, `unsupported`), a fonte
  (README, issue ou PR do motor) e a data da conferência. As placas sem relato ficam como
  `untested`, com aviso. O RTL8821CE também é `recommended`, porque o README do frlg-ldn-trade o
  lista com confiabilidade alta. O catálogo muda junto com as versões do app (sem atualização
  automática, D17).
- **Placa ↔ catálogo.** A comparação é pelo nome do módulo ou do driver em
  `/sys/class/ieee80211/*/device/driver`, porque o módulo (`rtw88_8821ce`) e o driver
  (`rtw_8821ce`) podem ter nomes diferentes. Os conversores USB-serial são identificados por
  VID:PID.
- **Leitura do sistema** (`LinuxProbe`). Só leitura de `/sys`, `/proc` e `/etc`, sem root. A raiz é
  configurável, e os testes montam uma árvore falsa. Os links do sysfs são relativos, por isso o
  caminho é resolvido componente a componente, como o `realpath`.
- **Verificações** (`SetupChecks.Evaluate`). Cada uma tem código, argumentos, etapa, status e se é
  essencial. Quando algo falta, o app mostra o comando de correção (`usermod`, `brltty`, regra
  udev) para o usuário copiar e nunca o executa. As verificações que dependem de entregas futuras
  (worker, teste da placa, teste com o Switch, firmware do ESP32) aparecem como "em breve". A troca
  real só fica liberada (`ReadyToTrade`) quando todas as essenciais estão OK. O motor simulado
  continua livre.
- **prod.keys.** A validação imita `ldn.load_keys`:
  - `aes_kek_generation_source`, `aes_key_generation_source` e `master_key_00` ou `master_key_12`;
  - 16 bytes em hex.

  O arquivo é gravado já com o modo 600 (`UnixCreateMode`), sem janela de tempo com outra
  permissão. A API e o relatório informam só se o arquivo é válido e o que falta. Nunca aparecem
  valores nem os nomes das chaves presentes.
- **Relatório** em markdown para pedir ajuda: sistema, placas, portas e verificações, sem nome de
  usuário, caminhos pessoais ou chaves.
- **Simulação** só com `ASPNETCORE_ENVIRONMENT=Development`. Nos outros ambientes, as rotas
  `/api/setup/scenarios` e `/api/setup/simulation` respondem 404.

## D15. Worker privilegiado e motor da placa Wi-Fi (Entrega 6)

- **Worker em Python, não em C#.**
  - Ele roda no mesmo Python embutido que o motor precisa e importa o `frlgtrade` diretamente, sem
    um segundo runtime nem o `clang` para AOT.
  - O esboço C# de `worker/TradeRock.Worker` foi removido.
- **Canal: stdin/stdout do processo do `pkexec`, em vez de socket Unix.**
  - Só quem iniciou o worker fala com ele, sem arquivo de socket nem checagem de UID.
  - Quando o app sai, o EOF faz o worker cancelar, devolver o rádio e sair.
- **Polkit.**
  - `allow_active=auth_admin_keep`: a senha é pedida pelo sistema, uma vez por sessão do app.
  - `allow_any` e `allow_inactive` ficam em `no`.
  - A política autoriza só o launcher, pela anotação `org.freedesktop.policykit.exec.path`.
  - O launcher ignora argumentos e roda `python3 -I -B worker/run.py`.
- **Tudo que roda como root é do root e somente leitura** (`scripts/install-worker.sh`).
  - O app confere com `access(W_OK)` que nem o launcher nem as pastas acima dele são graváveis pelo
    usuário; se forem, recusa (`worker_unsafe`).
  - Em desenvolvimento: `/usr/local/lib/traderock-dev` (ação `...worker.dev`).
  - No .deb: `/usr/lib/traderock`. No AppImage: `/usr/local/lib/traderock` (D17).
- **Motor sem fork.** O submódulo fica intocado. Os dois patches do FRLG-LDN-Trade-Console (AGPL,
  creditados) são aplicados numa cópia durante o build:
  - varredura com dwell de 0,6 s nos canais 2,4 e 5 GHz;
  - espera pela aprovação;
  - monitor auxiliar para o mt76x0u;
  - correção do `save_received(args)`.

  O wrapper (`trade_child.py`) monta a configuração com os dataclasses de `frlgsim/config.py`, troca
  `trade_runtime.ConsoleLog` por um logger que emite eventos e envolve `save_received` para mandar
  cada recebido no commit. Um teste confere que cada frase mapeada e cada assinatura usada ainda
  existem no submódulo.
- **Retry no TradeRock.** O worker define `FRLG_JOIN_ATTEMPTS=1`, para que cada tentativa do
  coordenador seja uma tentativa do motor. Define também `FRLG_JOIN_IP_WAIT=100`, abaixo dos 120 s
  de aprovação do coordenador, para o motor desistir primeiro com o código `join_not_approved`.
- **Rádio devolvido.** O upstream derruba a interface normal da placa e não a religa. O worker anota
  o estado (ligada? gerenciada pelo NM?) antes de cada operação e restaura no fim, depois de apagar as
  interfaces `ldn*`.
- **Teste da placa.** Usa a API da biblioteca LDN (`create_monitor`, `set_channel`, `send_frame`,
  `recv`) nos canais 1, 6 e 11. Transmitir sem erro não prova que o quadro saiu no ar; isso exigiria
  um segundo rádio. O resultado vale para o phy e o driver testados, já que `phyN` pode virar outra
  placa.
- **NetworkManager + udev.** São dois arquivos:
  - `zz-ldn-unmanaged.conf`, com `[device-traderock-ldn] match-device=interface-name:ldn*
    managed=0`, uma seção própria que não sobrescreve `unmanaged-devices` de outros arquivos;
  - `10-traderock-ldn.link`, com `OriginalName=ldn*` e `NamePolicy=`, que resolve o problema do
    PR #9 do upstream (o udev renomeava `ldnclient` para `wlx<mac>`).

  Recarregados com `nmcli general reload conf` e `udevadm control --reload`, sem derrubar a conexão.
- **`iw` e `ip` embutidos.** O motor chama os dois pelo nome, e sem eles quebrava no meio com
  `FileNotFoundError`.
  - `scripts/build-nettools.sh` compila os dois estáticos com musl usando o Zig 0.16.0, a partir das
    fontes oficiais com sha256 conferido: iw 6.17 + libnl 3.12.0 e iproute2 7.2.0.
  - Não depende de nada do sistema (nem gcc, nem pkg-config, nem bison), então roda igual no CI e no
    host.
  - Os binários ficam em `<instalação>/bin`, que o worker põe na frente do PATH dos filhos. Juntos têm
    ~1,5 MB.
  - O `sysctl` (procps) continua vindo do sistema, porque está em praticamente todo Linux; se faltar,
    o assistente mostra o comando.
  - O `pkexec` não pode ser embutido: é setuid root e depende do polkit do sistema. Ele vira
    dependência do .deb na Entrega 8.
- **Teste com o Switch (etapa 6).** Na Entrega 6 ele ainda não existia e não bloqueava a troca. Desde a
  Entrega 7 (D16), ele é essencial com a placa Wi-Fi.
- **Runtime embutido.**
  - cpython 3.12.14 do python-build-standalone (release 20260929, sha256 fixo).
  - Dependências com `pip --require-hashes` (`worker/requirements.lock`).
  - Sem Tk, IDLE, testes, `libpython` compartilhada nem `pip`: ~100 MB.
  - Só os módulos de extensão passam por `strip`, porque o binário e a `libpython` quebram com o
    `strip` do binutils.

## D16. Teste com o Switch pela placa Wi-Fi (Entrega 7)

- **Só escuta, sem entrar na sala.** O processo filho `worker/traderock_worker/beacon_child.py` usa a
  API da biblioteca LDN (ldn 0.0.17), conferida no código-fonte:
  - `wlan.create_factory` e `Factory.create_monitor` criam o monitor;
  - `Monitor.set_channel` passa pelos canais;
  - `ldn.Scanner(derivations, monitor).receive()` devolve cada anúncio que decifrou, com
    `KeyDerivation` para os protocolos LDN 1 e 3.

  É o mesmo que o `ldn.scan` faz por dentro. A diferença é que o teste repete as voltas até achar a
  sala ou o tempo acabar, e segue quando um canal falha (placa sem 5 GHz), em vez de abortar.
- **Anúncio que não decifra.** A biblioteca registra "Failed to parse advertisement frame" no logger
  `ldn` (D5). O teste conta esses avisos por canal. Depois do primeiro, faz mais uma volta completa
  (a sala do FRLG pode decifrar em outro canal) e para, porque com chaves erradas esperar não muda
  nada.
- **Canais e tempo por canal iguais aos da troca:** 1, 6, 11, 36, 40, 44 e 48, com 0,6 s em cada
  (`FRLG_CHANNELS` e `FRLG_SCAN_DWELL` do patch 0001). Assim, "achou no teste" significa "a troca vai
  achar". Um teste em Python confere esses valores no patch.
- **Sala do FRLG** reconhecida pelas regras do motor, importadas do `frlgsim.transport` com os
  patches, sem lista própria:
  - `LiveTransport.LOCAL_COMMUNICATION_ID` e `KNOWN_COMM_IDS`;
  - ou `_looks_like_frlg_beacon`.
- **Resultados** (`verdict`), nesta ordem:
  1. `found_decrypted`: a sala foi decifrada. É o único que aprova.
  2. `found_not_decrypted`: algum anúncio não decifrou. Vence `other_session`, porque a sala pode usar
     o outro protocolo LDN. Se o `prod.keys` só tem a `master_key_00`, a verificação acrescenta a dica
     `missing_master_key_12`.
  3. `other_session`: as chaves decifraram outro jogo, mas a sala do FRLG não apareceu. Não aprova
     (decisão do autor).
  4. `not_found`.

  Falhas do rádio: `monitor_failed`, `channel_failed` e `scan_failed`. Há ainda `cancelled`.
- **Duração:** até 30 s (`SetupService.SwitchTestSeconds`), parando assim que acha a sala.
  - Cancelar no app aborta o pedido HTTP; o `WorkerHost` manda `cancel` e o worker interrompe o filho
    com SIGINT.
  - Como no teste da placa, o rádio volta ao estado anterior antes da resposta.
  - Um teste cancelado não apaga o resultado anterior.
- **Chaves:** vão como bytes, como na troca. Ficam numa pasta temporária do root com permissão 600
  e são apagadas no fim. O resultado não traz nada das chaves nem endereços MAC; o relatório traz o
  código, o comm id e o canal.
- **Validade:** o resultado fica no estado do assistente e vale para a placa (phy + driver) e para o
  `prod.keys` testados.
  - O `prod.keys` é identificado pela data de gravação do arquivo (`KeysValidation.SavedAt`), nunca
    pelo conteúdo.
  - Enviar o arquivo de novo pede um teste novo.
  - O resultado não expira com o tempo (decisão do autor).
- **Essencial com a placa Wi-Fi.** O `ReadyToTrade` só libera a troca real depois do teste. Com o
  ESP32, o teste continua "em breve" (Entrega 9).
- **Protocolo do worker 2** (versão 0.7.0), por causa da operação nova `beacon_test`. Um worker
  instalado de uma versão anterior é recusado com `worker_version`, e o `dev-host.sh` reinstala.
- **Simulação** (só em desenvolvimento): a etapa 6 tem um seletor para escolher o resultado do teste.

## D17. Pacotes Linux e CI (Entrega 8)

- **Sem verificação de atualizações** (decisão do autor). Nem o app nem o catálogo consultam a
  internet: quem quiser baixa a versão mais recente na página de Releases. O catálogo embutido é
  atualizado a cada versão.
- **.deb** (`scripts/package-linux.sh`):
  - app *self-contained* em `/usr/lib/traderock/app`;
  - worker em `/usr/lib/traderock`, o local "release" que o app já procurava;
  - tudo do root pelo próprio pacote;
  - dependências com alternativas para Ubuntu 22.04/24.04 e Debian 12/13 (nomes com `t64`, ICU 70 a
    76). O ICU e o OpenSSL são carregados pelo .NET; o WebKitGTK, pelo Photino.
- **AppImage.** O polkit não aceita um `exec.path` dentro da montagem do AppImage (o caminho muda a
  cada execução e é do usuário). Por isso o worker vem embutido (`worker-payload`) e é instalado
  pelo app, com confirmação:
  - em `/usr/local/lib/traderock`, com a ação `io.github.traderock.worker.appimage`, para não brigar
    com o .deb; rodando do AppImage, o app prefere essa cópia;
  - por `pkexec /bin/sh -c "<script>" worker-setup install <pasta> <política>`, com o worker como
    `.tar` na entrada padrão. O script (`packaging/linux/worker-setup.sh`) fica embutido no app e vai
    na linha de comando. Nenhum arquivo gravável pelo usuário é executado como root, e o root não
    precisa ler a montagem FUSE, que o kernel só libera para o dono;
  - o script extrai numa pasta temporária ao lado, confere o launcher e a política, põe dono root e
    tira a escrita de outros, e troca a pasta antiga com um `mv`. Se algo falha, a instalação
    anterior fica intacta;
  - o `PAYLOAD-ID` (versão + sha256 do conteúdo) diz se a cópia instalada é a deste AppImage; se não
    for, o assistente mostra "atualizar";
  - a remoção fica em Configurações. Antes de instalar ou remover, o worker da sessão é encerrado;
  - o pkexec usa a ação padrão do polkit (`org.freedesktop.policykit.exec`, `auth_admin`). A senha é
    pedida pelo sistema.
- **Runtime do AppImage:** `type2-runtime` estático (sem libfuse2), squashfs com zstd. O upstream só
  publica o "continuous"; o sha256 fica fixo no script e o arquivo fica no cache do CI. Se o
  upstream mudar, o build falha com uma mensagem clara.
- **Versão:** `Directory.Build.props` (0.8.0), ou a tag no CI (`v0.8.1` → 0.8.1). Fora de tag, o CI
  usa `0.8.0-ci.<execução>`. O comando `traderock --version` não cria pasta de dados nem banco.
- **CI:**
  - `package` monta os pacotes na imagem do devcontainer, com cache dos downloads (todos com sha256);
  - confere no `.deb` que todo arquivo é do root e que nada é gravável por grupo/outros;
  - `smoke-deb` instala o .deb em Ubuntu 22.04/24.04 e Debian 12/13 limpos, roda o app como usuário
    comum, confere que ele acha o worker (seguro, com `iw`/`ip` embutidos), fala com o worker como
    root, remove o pacote e confere que os dados do usuário ficaram;
  - `smoke-appimage` roda o AppImage (extrair e rodar, sem FUSE) e instala e remove o worker embutido
    com o mesmo script do app;
  - a tag `v*` publica o Release direto (decisão do autor), com o .deb, o AppImage, o `SHA256SUMS`, o
    código-fonte completo com os submódulos (AGPL) e as fontes do `iw`, `iproute2` e `libnl` (GPL-2.0
    e LGPL-2.1).
- **Tela Sobre** (`/sobre`, link no rodapé): versão, licença, oferta do código-fonte e a tabela do
  `THIRD-PARTY-NOTICES.md`, lida no build.
- **Não verificado:** o AppImage montado por FUSE, o diálogo do pkexec e a janela nativa. O teste
  deste ambiente usou "extrair e rodar" e rodou o script de instalação como root, sem pkexec.

## D18. ESP32: motor, firmware e teste com o Switch (Entrega 9)

- **Núcleo do easyworld com patch.** O motor usa o `host/core` do easyworld (AGPL), sem fork e sem
  copiar código. `app/Frlg.Trade.Core.Patched` copia as fontes para `obj/` a cada build e aplica
  `engines/patches/frlg-ldn-trade-esp32` (`patch -p1`), como o motor Python (D15). O patch:
  - torna o treinador simulado configurável (nome, TID/SID, FireRed/LeafGreen). O padrão reproduz os
    bytes originais; os campos seguem `frlgsim/linkplayer.py` e `ni.py`;
  - faz `Rfu.Name` codificar a pontuação Gen III;
  - acrescenta o limite de trocas (`tradeLimit`): depois de N trocas, o motor sai da sala pelo mesmo
    caminho de quando o Switch cancela (decisão do autor, opção a);
  - faz `TradeSession.Run` receber o `KeyFile`, o limite e o treinador;
  - aceita os dois comm ids do FRLG já vistos (`KnownIds`). O easyworld só aceitava
    `0x01006fa0233f8000`; o motor Wi-Fi e os vetores de teste do easyworld usam `0x0100610011000000`.
- **Eventos.** Os marcos do `TradeSession` (fases em chinês, logs e eventos) viram os eventos do
  TradeRock (`Esp32EventMapper`). As frases reconhecidas ficam em `Esp32Messages`, e um teste confere
  que cada uma ainda existe no submódulo. O log detalhado fica em `logs/esp32/<sessão>/native.log`,
  com as 10 últimas sessões guardadas.
- **Cancelar** para a sessão pelo `LDN_STOP` do firmware. Diferente do motor Wi-Fi, não há a saída
  educada da sala (o núcleo não tem): o Switch vê a conexão cair.
- **Firmware.** Os quatro binários completos de `firmware/release` do easyworld vão embutidos no app
  (3,7 MB) e são conferidos pelo `SHA256SUMS.txt` a cada uso. Não compilamos nada com o ESP-IDF.
  - **`LDN_HELLO`** sempre (é rápido e não reinicia a placa), na hora de escolher a porta: diz se há
    firmware compatível e de qual chip. Ele também é o teste de comunicação (`HELLO` + `BEGIN` +
    `STOP`), que substitui o antigo item `esp32_serial_test`.
  - **esptool** só a pedido (decisão do autor): identificar (chip, revisão e flash), conferir a
    versão (`verify_flash` com o binário embutido) e gravar.
  - A gravação primeiro identifica o chip e só grava o binário dele, numa flash do tamanho certo.
    Depois confere e faz o `LDN_HELLO`.
  - ESP32 clássico com revisão < 3 recebe um aviso: o WROOM-32E usa o D0WD-V3, então uma revisão
    menor indica o WROOM-32 antigo. A confirmação manual do módulo continua.
- **esptool 5.4.0** no Python embutido do worker, com hash fixo (`worker/requirements.lock`, +44 MB).
  - Roda como o usuário, sem pkexec: `run.py --esp32`. O launcher do polkit ignora argumentos, então
    esse modo nunca roda como root.
  - Usa a API pública (`esptool.cmds` + logger próprio para o progresso), não a leitura de texto.
  - O Python vem do `worker-payload` do AppImage, do worker instalado ou, em desenvolvimento, de
    `build/worker/stage`.
  - O `cryptography` exige glibc ≥ 2.34 (Ubuntu 22.04 tem 2.35).
- **Teste com o Switch pelo ESP32.** `LDN_SCAN` nos canais 1, 6, 11 e 2–10 (a mesma ordem e os
  mesmos 500 ms da troca), decifrando os anúncios `LDN_ADV` com o prod.keys (`LdnKeys.Decode`),
  sem entrar na sala.
  - Resultados e regra de parada iguais aos da D16.
  - O resultado vale para a porta, o chip do firmware e o prod.keys.
  - O teste passa a ser essencial também com o ESP32.
  - O ESP32 não enxerga salas em 5 GHz; o assistente explica isso.
- **Uma operação por vez na porta.** Firmware, esptool e teste recusam com `esp32_busy` ou `busy` se
  houver outra operação ou uma troca em andamento.
- **Testes sem hardware:**
  - um ESP32 falso em Python (`app/TradeRock.Tests/fake_esp32.py`) fala o protocolo serial de
    verdade (COBS + CRC32) num pseudo-terminal, e o `SerialDevice` real conversa com ele;
  - os anúncios cifrados vêm dos vetores sintéticos do próprio easyworld (`host/tests/fixtures`);
  - o esptool é testado com um falso que tem a mesma API, e um teste confere essa API no esptool real
    quando ele está instalado.
- **Não verificado:** placa real (gravação, troca e teste com o Switch). Os binários do S3, C6 e C3
  publicados pelo easyworld também não foram regravados numa placa para uma troca real, segundo as
  notas de versão deles.

## D19. Modo servidor na rede (Entrega 10)

Com o app aberto, a mesma interface é servida para a rede da casa (celular, outro PC), com contas.

- **Liga e desliga sem reiniciar.** O endereço local (127.0.0.1, porta aleatória, token da D1) não
  muda. O da rede entra como um endpoint da seção `Kestrel` da configuração, que o Kestrel já recarrega
  sozinho (`reloadOnChange`), e o IP entra nos hosts aceitos (a filtragem de hosts também recarrega).
  - Escuta **só no IPv4 da interface escolhida**, nunca em `0.0.0.0`. Porta padrão 8337 (1024–65535).
  - Começa **desligado a cada abertura** do app; a interface e a porta ficam guardadas.
  - Desligar derruba todos os logins da rede e tira o IP dos hosts aceitos.
  - Porta ocupada ou endereço sumido: o app espera até 5 s pelo endpoint e avisa `bind_failed`.
- **Nunca na placa Wi-Fi das trocas.** Com o rádio Wi-Fi (assistente), as interfaces do phy escolhido
  ficam bloqueadas (sem phy escolhido, todas as sem fio). Sem outra interface, o app pede um cabo ou um
  segundo adaptador. Com o ESP32, qualquer interface serve. Escolher o motor Wi-Fi com o modo servidor
  numa interface dessa placa é recusado (`server_mode_on_radio`).
- **HTTP, não HTTPS** (decisão do autor). Um certificado próprio assustaria no celular. A tela avisa
  que é só para a rede de casa, que a porta nunca deve ser liberada no roteador e que a senha trafega
  aberta na rede local.
- **Quem é quem** (`SessionAuthMiddleware` + `AccessPolicy`). A requisição é "da rede" quando chega
  pelo IP e pela porta do modo servidor.
  - O token da janela **não vale pela rede**.
  - A página abre sem login, mas a API pede o login de uma conta (cookie `traderock_lan`, HttpOnly,
    SameSite=Strict, só na memória, 12 h sem uso).
  - Toda alteração pela rede exige o cabeçalho anti-CSRF `X-TradeRock-CSRF` (token da sessão, recebido
    em `/api/auth/me` e no login) e, se o navegador mandar `Origin`, ele precisa ser o próprio endereço.
- **Só na janela local**, mesmo para administradores pela rede: assistente (inclui o `prod.keys`, que
  nunca trafega pela rede), worker, liga/desliga do modo servidor, fechar o app, diálogos nativos
  (abririam no PC), escolha do motor e configurações de troca. Pela rede, `/api/health` responde
  `window: false`.
- **Contas.** Argon2id (Konscious 1.3.1; o .NET não tem Argon2) com os parâmetros mínimos da OWASP
  (m=19 MiB, t=2, p=1), sal aleatório, formato PHC.
  - Papéis: administrador (gerencia contas, tira pessoas da fila, cancela a troca dos outros) e usuário.
  - A conta do **dono do PC** (id fixo `…0001`) é a da janela local: sempre administradora, não pode
    ser apagada nem desativada. Ela entra pela rede só se ganhar nome e senha.
  - Apagar uma conta apaga o cofre dela. Trocar a senha, desativar ou mudar o papel derruba os logins.
  - Login errado não diz se a conta existe (mesmo tempo, com um hash falso).
- **Freios.**
  - Login: 10 erros por endereço em 15 min bloqueiam o endereço, e 5 erros seguidos travam a conta por
    5 min (HTTP 429).
  - Limite geral de 600 requisições por minuto por endereço da rede (`RateLimiter` do ASP.NET Core). A
    janela local não tem limite.
- **Um cofre por conta.** Pokémon, caixas, tags, histórico e treinadores salvos ganharam `OwnerId`.
  - O `TradeRockDb` filtra por dono (filtro global do EF Core, lido de `OwnerScope` na criação do
    contexto) e carimba o dono no que é criado. O dado antigo ficou com o dono do PC (migração
    `Accounts`).
  - "Meu treinador" vira uma chave por conta (`trainer.me@<id>`).
  - Os arquivos `.pk3` continuam endereçados pelo hash e são compartilhados entre cofres: só saem do
    disco quando nenhum cofre os usa.
  - Conta nova começa com os Pokémon de demonstração.
- **Fila** (`TradeQueue`, na frente do `TradeCoordinator`). Um rádio, uma troca por vez, na ordem de
  chegada, e **a troca começa sozinha quando chega a vez** (decisão do autor).
  - Uma entrada por conta. A janela local também entra na fila. O modo demonstração também passa por
    ela.
  - Todos veem a fila (nome e posição).
  - Cada conta só vê a própria troca: `/api/trades/current` e o SSE filtram pelo dono da sessão.
  - O recebido e o histórico vão para o cofre de quem pediu.
  - Sair da fila: quem pediu ou um administrador. A troca em andamento é cancelada por quem pediu ou por
    um administrador.
- **Não verificado:** celular de verdade na rede. Testado com o Kestrel real em `127.0.0.2` (liga,
  desliga e porta ocupada) e, no WSL, por curl no IP da `eth0`.

## D20. Windows (Entrega 11)

Windows 10 1809+ e 11, x64, **só o motor ESP32** (o Windows não dá acesso de baixo nível ao Wi-Fi).

- **Compilado no Linux.** `scripts/package-windows.sh` publica o app para `win-x64` (self-contained)
  daqui mesmo, com o mesmo núcleo do easyworld e os mesmos patches do Linux. Só o Inno Setup (instalador)
  e a execução dos testes precisam do Windows. No CI, um job Linux monta `build/windows` (app + testes) e
  um job Windows roda os testes, gera o instalador e faz a fumaça (`scripts/smoke-windows.ps1`: instala em
  silêncio, confere versão, firewall, esptool, API e ACL, e desinstala). Os testes que dependem do Linux
  (worker, pkexec, `/sys`, pseudo-terminal) usam `[LinuxFact]` e são pulados no Windows.
- **Instalador Inno Setup, com administrador** (decisão do autor). Instala em Arquivos de Programas e
  cria a regra do Firewall do Windows para o modo servidor: entrada TCP, **só o `traderock.exe` e só no
  perfil "Privada"**. A regra sai na desinstalação. O "Abrir o TradeRock" do fim roda como o usuário
  (`runasoriginaluser`): o app nunca roda como administrador.
- **Sem assinatura digital por enquanto** (decisão do autor). O Windows mostra o aviso do SmartScreen na
  primeira vez; o README explica o "Mais informações → Executar assim mesmo" e o `SHA256SUMS` do Release
  inclui o instalador.
- **Dados em `%LOCALAPPDATA%\TradeRock`**, não em `%APPDATA%` (que acompanha o perfil móvel: o prod.keys
  não deve viajar). A pasta de dados e a de chaves ganham uma ACL **sem herança, só com o usuário atual**
  (equivalente ao 700/600 do Linux); o que é criado dentro herda. A desinstalação não apaga os dados.
- **Leitura do sistema pelo WMI** (`System.Management`, `Win32_PnPEntity`): portas COM com VID/PID e
  nome do conversor, e conversores **sem driver** (código 28 do Gerenciador de Dispositivos), que não
  ganham porta COM. O aviso `usb_driver_missing` passou a independer da porta escolhida e só aparece para
  conversores do catálogo, com o link do fabricante (WCH, Silicon Labs, FTDI).
- **esptool no Windows.** CPython 3.12 do python-build-standalone para Windows (sha256 fixo, sem `.pdb`,
  Tcl/Tk e testes) em `esp32-runtime/`, com as rodas `win_amd64` de `worker/requirements-esp32-windows.lock`
  (hash fixo; inclui o `colorama`, que o rich-click só exige no Windows). O esptool só existe como
  código-fonte: vira uma roda `py3-none-any` a partir do sdist conferido pelo `requirements.lock`. O pip do
  Python do Linux instala as rodas do Windows numa pasta, sem executá-las.
  - No Windows, "acesso negado" ao abrir a COM é **porta em uso** (não há grupo de permissão).
  - Pedido e respostas em UTF-8 (caminhos com acento), sem janela de console.
- **Janela.** Executável `WinExe` (sem console) com ícone (`packaging/windows/traderock.ico`, gerado por
  `make-icon.py` a partir das formas do `icon.svg`). Chamado de um terminal (`--version`, `--no-window`),
  escreve no console de quem chamou. **Sem WebView2** (Windows 10 antigo): mensagem com o link oficial da
  Microsoft e o app abre no navegador padrão; nada é baixado.
- **Modo servidor no Windows.** Adaptadores virtuais (Hyper-V, WSL, VirtualBox, VMware, Docker) aparecem
  marcados como "virtual" e no fim da lista. A tela avisa que a regra do firewall vale só para redes
  "Privadas".
- **Verificado no Windows 11 real** (via WSL): 247 testes passando (31 pulados, só Linux), janela com
  WebView2, API, ACL do prod.keys, esptool (porta ausente) e a fumaça completa do instalador.
  **Não verificado:** ESP32 real no Windows (gravação, troca, driver do CH340/CP210x).

