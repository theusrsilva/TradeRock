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
- **Runtime do AppImage:** `type2-runtime` estático (sem libfuse2), squashfs com zstd. Usa a versão
  fixa `20251108` do upstream (o "continuous" mudou em outubro de 2026 e quebrou o CI da v01.00.00);
  o sha256 fica fixo no script e o arquivo fica no cache do CI.
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


## D21. Jogos do Switch pelo pokeldn (Entrega 13)

Plano e respostas do autor em [multijogos.md](multijogos.md).

- **pokeldn como submódulo**, em `engines/pokeldn`: [Decryptu/pokeldn](https://github.com/Decryptu/pokeldn)
  v0.49.1, commit `ae97616`, AGPL-3.0. É um fork do frlg-ldn-trade que fala o protocolo local de cada jogo.
  - Sem fork nosso (D2). `scripts/stage-pokeldn.sh` copia o pokeldn e aplica `engines/patches/pokeldn`.
  - A cópia do worker leva só `pokeldn/`, `bin/`, o LDN vendorizado, `config/`, a versão do firmware e as
    licenças. São 15 MB, sem a interface gráfica (flet), o firmware em C nem os testes.
- **Python 3.12 mantido.** O plano previa o 3.13, mas não é preciso:
  - o LDN vendorizado exige 3.12 (sintaxe da PEP 695);
  - não há nada exclusivo do 3.13;
  - a suíte do pokeldn dá o mesmo resultado nos dois.

  Os módulos de protocolo de todos os jogos importam no Python embutido com as dependências que o
  `requirements.lock` já tem (pycryptodome, trio, zstandard, pyserial, python-netlink).
- **Duas bibliotecas LDN.**
  - O pokeldn traz uma cópia modificada do kinnay/LDN 0.0.17 (`vendor/LDN`, rev `39d0b20`, com correções
    de placas). O processo do pokeldn põe essa cópia na frente do `sys.path`.
  - O motor do FRLG continua com o `ldn` do PyPI, no próprio processo.
- **Modo TradeRock (`TRADEROCK=1`, patch 0001).** Sem a variável, o pokeldn se comporta como o upstream,
  e os testes dele continuam valendo.
  - `app/update._open` recusa. Por ele passam atualizações, sprites, loja de plugins e galeria de eventos.
  - `plugins.load` não carrega nada.
  - `prepare_offer` pula os plugins e o ajudante `pokeldn-pkhex`. Sem o patch, um Pokémon ilegal
    bloquearia a troca, e o autor quer só o aviso.
  - O app confere o Pokémon com o PKHeX dele e manda a forma de equipe.
- **Trava de rede (`worker/traderock_worker/netguard.py`).** É a segunda camada da D17: um audit hook
  (PEP 578) instalado no processo do pokeldn.
  - Recusa `connect`, `sendto` e `sendmsg` IP para fora da rede local (privada, link-local, loopback,
    multicast, broadcast).
  - Recusa também resolver nomes de host.
  - Netlink, AF_PACKET e serial não são IP e passam.
  - A troca online do pokeldn (Nostr, `--online`) nunca é usada.
- **Chaves.**
  - O pokeldn não registra o prod.keys.
  - Ele imprime **chaves de sessão derivadas** (`SessionKeys.__repr__`, `pia_lan`, as chaves de canal do
    PLA) e as grava no `--capture`.
  - O adaptador não guarda a saída crua: guarda só os eventos mapeados e o resto com as sequências
    hexadecimais longas apagadas. Nunca usa `--capture`.
  - O ESP32 recebe só a chave de rede derivada, como no easyworld.
- **Adaptador (Entrega 15).**
  - **Um processo filho por sessão.** O pokeldn guarda estado global: o contador `_done` de
    `pokeldn.ldn` e o `TRADE_IN_PROGRESS` do LGPE.
  - **Os scripts `bin/<jogo>_<papel>.py` são a API.** A lógica de rádio e Pia de SV, SwSh (join), BDSP
    (join) e LGPE está presa no `main()` deles.
    - O filho carrega o script com `importlib` e chama `main(argv)` com as flags testadas do catálogo do
      pokeldn (`pokeldn/app/catalog.py`).
    - Sempre com `--no-take-host`: o padrão `--take-host` do SV e do PLA faz `os.execv`.
    - O `main()` do `pla_host` lê `sys.argv`.
  - **Eventos.** São as linhas da saída padrão:
    - `[done] trade N complete`;
    - `[link] waiting|joined|left`, com `POKELDN_MANAGED_RUN=1`;
    - os prefixos de cada jogo.

    Um teste confere que cada frase mapeada existe no submódulo, como o do frlg-ldn-trade.
- **Por jogo** (versões em que o pokeldn estudou cada jogo; nenhum confere a versão do console):

  | Jogo | Comm id | Versão | Papéis | Código | Oferta | Recebido (arquivo) | LAN |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | FRLG | 0x01006fa0233f8000 | rev 0x0A | host e join | não tem | PK3 | PK3 decifrado | não |
  | Let's Go | 0x010003F003A34000 | 1.0.2 | host e join | 3 Pokémon (host) | PB7 | 232 bytes + 28 zeros | não |
  | Sword/Shield | 0x0100ABF008968000 | 1.3.2 | host e join | 8 dígitos (host) | PK8 de equipe | PK8 cifrado, 344 | **sim** |
  | BDSP | 0x0100000011D90000 | 1.3.0 | host e join | senha (host) | PB8 | PB8 cifrado, 328 | não |
  | Arceus | 0x01001f5010dfa000 | 1.1.1 | host e join | 8 dígitos | PA8 de equipe | PA8 cifrado, 376 | não |
  | Scarlet/Violet | 0x0100a3d008c5c000 | 4.0.0 | host e join | até 16 caracteres | PK9 de equipe | PK9 decifrado, 344 | não |
  | Legends: Z-A | 0x0100f43008c44000 | 2.0.2 | host e join | 8 dígitos | PA9 de equipe | PA9 decifrado, 344 | não |

  - O app normaliza o recebido com o PKHeX.
  - SwSh e BDSP gravam o arquivo quando o console **mostra** a oferta, antes de a troca concluir. Por
    isso o app só guarda o Pokémon depois do evento de troca concluída.
  - Várias trocas por sessão em todos os jogos. A última oferta se repete, menos no LGPE, que para de
    responder.
- **Modo LAN só no Sword/Shield.** O plano previa também o Scarlet/Violet, mas a troca do SV nunca usa
  LAN. O mesmo vale para Z-A, PLA, BDSP e LGPE.
  - No console: Opções, L + R + analógico esquerdo.
  - Sem root, sem prod.keys e sem rádio.
  - UDP 30000, 40000 e 49152–49155. O código de troca é ignorado.
- **Evolução por troca.** Quem evolui é o console, no que recebe; o pokeldn não evolui nada.
  - O `--trade-back` devolve o Pokémon recebido para ele evoluir no console. Só foi verificado em FireRed
    e Shield.
  - Se acontece algo parecido com a D8 nos outros jogos, só o teste com o console dirá.
- **Sair no meio castiga o console.**
  - PLA: trava de cerca de 10 min.
  - LGPE: 600 s de jogo.
  - BDSP: penalidade.
  - Z-A: "Error Number: 6" quando a sessão fecha sem passar o host.

  O cancelar do app pede a saída educada de cada jogo e espera.
- **Rádios.**
  - **Placa Wi-Fi no Linux:** root, pelo worker. O pokeldn chama esse caminho de "legacy".
  - **ESP32:** o caminho principal dele, sem root.
    - Serial COBS + CRC32, protocolo 1, 115200 → 921600 baud. Firmware 1.9.1 no commit travado.
    - Placas: ESP32 clássico, S3, C3 e C6. Não servem o ESP32-S2 nem o ESP32-WROOM-32 genérico antigo.
    - O ESP32 não alcança os canais 36–48 de 5 GHz.
  - **`ldnrs`:** placa USB no Windows, nunca testada com console. Fica de fora.
- **Firmware do pokeldn.** Os `.bin` não estão no repositório, só nos Releases do GitHub, com `SHA256SUMS`.
  Na Entrega 17 escolho entre:
  - baixar no empacotamento, com sha256 fixo (nunca em tempo de execução, D17);
  - compilar com o ESP-IDF v6.1 no CI.
- **Ajudante PKHeX do pokeldn** (`services/pkhex`, o mesmo PKHeX.Core 26.8.26). Só é compilado para os
  testes. Ainda dependem dele:
  - `SERVICE.names(..., "bag")` do `sv_host` e do `swsh_host`;
  - o Mystery Gift do SwSh (`validate_gift`).

  Resolver nas Entregas 15 e 18.
- **Testes.** `scripts/test-pokeldn.sh` entrou no `ci.sh` (cerca de 5 min) e roda a suíte do pokeldn na
  cópia com os patches.
  - Os testes que falham só porque falta o flet contam como pulados (`scripts/pokeldn/skip_gui.py`).
  - No commit `ae97616`: 4667 passaram, 354 pulados.
  - `worker/tests/test_pokeldn.py` cobre os patches e a trava de rede.
- **Fora:**
  - Ruby/Sapphire/Emerald: em prévia no pokeldn, ainda não lançado no Switch.
  - Pokémon Champions e Pokopia: o pokeldn não fala com eles.

## D22. Base multijogo (Entrega 14)

Respostas do autor em [multijogos.md](multijogos.md): um "Meu treinador" por jogo, mover entre jogos como o HOME,
sprites até a Gen 9, cofre único com filtro, legalidade que avisa sem bloquear.

- **Catálogo** (`Core/Games/GameCatalog.cs`, espelhado em `frontend/src/lib/games.ts`). Os sete jogos, cada um com:
  - formato do PKHeX;
  - versões;
  - código de troca: nenhum no FRLG, três Pokémon no Let's Go e oito dígitos nos outros;
  - modo LAN (só SwSh);
  - atividades.

  O `TradeGame` ganhou as versões novas no fim, porque é gravado como texto no banco.
- **Leitura** (`PokemonReader`).
  - A Gen 3 continua igual ao `Pk3Reader`, com o mesmo hash.
  - Os outros formatos ficam guardados na forma "de caixa" decriptada (`SIZE_STORED`). Da Gen 8 em diante o PS atual
    fica nessa parte do arquivo, então o hash muda com o PS.
  - O `EntityFormat` do PKHeX confunde PB7 com PK7, PA9 com PK9, e PK8 com PB8. Por isso o jogo vem da extensão do
    arquivo ou de quem chama (troca, construtor, transferência). Sem isso, o arquivo é recusado com `format_ambiguous`.
    O Arceus (PA8) é reconhecido sozinho.
- **Cofre.** Colunas novas `Family` e `Form` (migração `Games`; os antigos ficam como `Frlg`).
  - O arquivo leva a extensão do jogo.
  - Filtro `?game=` na busca.
  - A exportação sai no formato do próprio jogo, decriptada. Os formatos `ek3` e 80/100 bytes são só da Gen 3.
  - A injeção em `.sav` e a importação de `.sav` continuam só no FRLG: os saves do Switch só saem do console com
    homebrew.
- **IDs de treinador.** O arquivo e o banco guardam TID16/SID16. A API e a tela usam os números que o jogo mostra
  (`TrainerIds`): da Gen 7 em diante, TID de 6 dígitos (ID32 % 1.000.000) e SID de 4 (ID32 / 1.000.000). Valem as
  mesmas regras para o "Meu treinador" por jogo (`trainer.me` no FRLG, `trainer.me.<jogo>` nos outros), os treinadores
  salvos, o histórico e o construtor. Nas linhas `TID:`/`SID:` do AutoMod, a partir do formato 7 ele lê esses números
  e reempacota.
- **Troca.**
  - Os jogos novos não têm time simulado. Cada Pokémon escolhido é uma oferta, de 1 a 6, uma por troca.
  - O pedido traz o código de troca.
  - O nome do treinador vai até 12 caracteres, sem a tabela Gen III.
  - Cada motor diz que jogos troca (`ITradeEngine.Supports`). Até a Entrega 15, só o motor de simulação troca os jogos
    novos: o Switch simulado devolve uma cópia do oferecido com o treinador "DEMO".
  - Nos jogos novos, Pokémon ilegal só pede a confirmação. No FRLG, a regra antiga continua (modo avançado mais
    confirmação).
- **Construtor.** O modo automático gera no formato do jogo escolhido (Pikachu legal verificado nos seis jogos novos).
  - Listas por jogo: espécies que existem no jogo, golpes sem os "dummied", e itens e bolas do save vazio.
  - Formas e habilidade oculta (posição 2).
  - EV até 252 e apelido de até 12.
  - O modo avançado (PID e IVs da Gen 3) continua só no FRLG. Nos jogos novos, editar no cofre gera o Pokémon de novo.
- **Mover entre jogos** (`HomeTransfer`, regras adaptadas do ajudante PKHeX do pokeldn, AGPL-3.0):
  - o Let's Go não recebe do HOME;
  - ovo fica no próprio jogo;
  - do FRLG, só sem item e sem HM;
  - a espécie e a forma precisam existir no destino;
  - o "Meu treinador" do destino vira o treinador atual;
  - golpe que o destino não tem é trocado pela sugestão do PKHeX, com as marcas de TM/TR.

  O registro do cofre é o mesmo: id, caixa, etiquetas e histórico.
- **Rastreador do HOME.**
  - Ele fica vazio, porque o HOME recusa um inventado e os jogos não o conferem.
  - O status de legalidade é "não verificável" quando o único problema é o rastreador ausente, ou quando o Pokémon
    seria válido com um rastreador. O PKHeX só aceita evoluir em outro jogo do HOME com rastreador.
- **Sprites.** 387–1025 do PokeAPI/sprites, commit `1dd55c0`, em `assets/sprites`, conferidos por `SHA256SUMS`
  (`scripts/fetch-sprites.sh`). As formas usam o sprite da forma normal.
- **Instruções de cada jogo** em `content/{pt-BR,en}/trade/jogos/<jogo>.md`, a partir dos passos que o pokeldn testou
  no console (`pokeldn/app/catalog.py`). Os nomes dos menus ficam em inglês, como no jogo.
- **Verificado:** testes de API e de núcleo nos sete formatos, e build e testes do frontend.
- **Não verificado:**
  - a interface no navegador ou na janela (sem Playwright nesta sessão);
  - qualquer troca real com os jogos novos (Entrega 15).

## D23. Motor pokeldn e ESP32 com o firmware do pokeldn (Entrega 15)

- **O TradeRock é sempre o anfitrião.** Pedido do autor: o jogador escolhe o código no console e digita o mesmo no
  app.
  - Em todos os jogos, o console que procura com um código entra numa sala anunciada com esse código (docs do
    pokeldn).
  - Por isso o app roda o `bin/<jogo>_host.py` (no FRLG, o `frlg_trade_host.py`). No BDSP, o código é a senha da
    Union Room.
  - A sala fica aberta 10 min (`RoomSeconds`). Se o console não entra, o resultado é `console_never_joined`, sem
    nova tentativa.
- **Comando.** O adaptador não copia as flags. Ele usa a própria função do pokeldn (`pokeldn.app.command.build`,
  com o catálogo dele), que monta as flags testadas no console. Ajustes nossos:
  - sem `--capture`, que grava chaves de sessão;
  - sem `--fresh-pid`, que passa pelo ajudante PKHeX do pokeldn e que o TradeRock não leva;
  - o código e o "trocar de volta";
  - no FRLG, `--trades` e `--version` (vale o último valor).
- **Processo** (`worker/traderock_worker/pokeldn_trade.py`, `run.py --pokeldn trade`).
  - Um processo por sessão. Ele roda o `main(argv)` do script, carregado com importlib.
  - Marcos: as linhas `[link] waiting|joined|left` e `[done] trade N complete`. Em cada `[done]` vai junto o arquivo
    mais novo da pasta de recebidos.
  - O log é a saída do pokeldn, com as sequências hexadecimais longas apagadas.
  - Cancelar manda SIGINT no Linux (o Ctrl+C do pokeldn sai com educação) e espera 20 s. No Windows não dá para
    mandar SIGINT a outro processo: o processo é encerrado.
- **Oferta.** O C# monta no formato do ajudante PKHeX do pokeldn (`PokeldnOffer`):
  - forma de equipe decriptada no FRLG e no Z-A;
  - forma de caixa criptografada no BDSP;
  - forma de equipe criptografada nos outros.

  No FRLG vão primeiro os oferecidos, na ordem das trocas, e depois o preenchimento.
- **Trocar de volta** (`--trade-back` do pokeldn, para o Pokémon do Switch que evolui por troca). Cada troca vira
  duas:
  - na primeira, o Switch manda o Pokémon;
  - na segunda, o app o devolve (ele evolui no Switch) e recebe de volta o que tinha mandado.

  Só a segunda entra no cofre, e é o mesmo arquivo, então não duplica. Vale em todo motor que roda o pokeldn
  (`ITradeEngine.UsesPokeldn`).
- **ESP32 só com o firmware do pokeldn (decisão da D21).**
  - Imagens 1.9.1 do Release `v0.49.1` em `assets/firmware/pokeldn`, conferidas por `SHA256SUMS.txt`
    (`scripts/fetch-pokeldn-firmware.sh`).
  - Gravadas em 0x0 com modo, frequência e tamanho da flash `keep`, como o pokeldn grava.
  - Placas: clássico (UART), S3, C3 e C6 (USB nativo), com no mínimo 4 MB.
  - O HELLO e a varredura do teste com o Switch também passam pelo pokeldn (`pokeldn_board.py`, `PokeldnEsp32Link`).
    O teste aceita a sala de troca de qualquer jogo do catálogo (`GameCatalog.CommunicationIds`).
  - O motor `esp32` agora é o `PokeldnEngine` e troca todos os jogos, o FRLG inclusive. No FRLG, o app cria a sala e
    o console entra pelo "Join Group", com outras instruções (`content/*/trade/jogos/frlg.md`).
  - O motor C# do easyworld (`Esp32SerialEngine`, `Frlg.Trade.Core.Patched`) saiu do registro de motores e fica no
    código até a limpeza.
- **Windows.**
  - O `esp32-runtime` leva o pokeldn (cópia com os patches) e os módulos do worker.
  - Leva também as rodas `win_amd64` do pokeldn (pycryptodome, trio e dependências, zstandard), com hash fixo.
  - O python-netlink, como o esptool, vira roda a partir do código-fonte conferido pelo `requirements.lock`. O LDN
    vendorizado o importa, mas só o usa na placa Wi-Fi.
  - Verificado pelo WSL no Python do Windows: todos os módulos de protocolo importam, e `run.py --pokeldn board|trade`
    roda de ponta a ponta até o erro esperado (porta COM inexistente, oferta falsa).
- **Placa Wi-Fi (Linux).**
  - O motor `python-ldn` troca todos os jogos: o FRLG continua no frlg-ldn-trade (o console é o líder) e os outros
    vão pelo pokeldn (`ITradeEngine.UsesPokeldn` vale por jogo).
  - O worker (root) ganhou a operação `start_pokeldn`. Ela grava as chaves num arquivo 600 do root, roda o
    `run.py --child pokeldn` (o mesmo `pokeldn_trade.py`, com `--phy` da placa escolhida e sem `POKELDN_RADIO`) e
    repassa cada mensagem como `{"event": "pokeldn"}`, terminando com `pokeldn_end`.
  - O rádio volta ao estado anterior no fim, como na troca do FRLG. As interfaces do pokeldn usam o prefixo `ldn*`,
    que a configuração do NetworkManager da D15 já deixa de fora.
  - Como anfitrião, o pokeldn precisa de uma placa que crie rede (modo AP). O teste da placa do assistente confere o
    modo monitor, não o AP: falta acrescentar.
- **Não verificado:**
  - gravação, HELLO e troca com uma placa real e um console real;
  - o cancelamento educado no Windows;
  - a troca dos jogos novos com a placa Wi-Fi (modo AP).

## D24. Modo LAN do Sword/Shield (Entrega 16)

- **É um motor a mais (`lan`, `PokeldnLanEngine`).** Respostas do autor: é uma conexão a mais e não mexe no rádio
  configurado.
  - Ele troca só o Sword/Shield, pela rede de casa: o pokeldn roda o `swsh_host.py --lan` (ferramenta
    `swsh-lan-host` do catálogo dele), como o usuário, no Linux e no Windows.
  - Não precisa de ESP32, placa Wi-Fi nem prod.keys (`ITradeEngine.NeedsSetup = false`), então a troca não pede o
    assistente concluído.
  - O jogo ignora o código de troca, que some da tela (`IgnoresLinkCode`) e não vai ao pokeldn.
  - Ele é escolhido no cartão "Rede de casa" de Configurações → Conexão. "Voltar" volta ao motor do rádio
    configurado ou à demonstração.
- **No console:** em Opções, L + R + analógico esquerdo liga o modo LAN. As instruções estão em
  `content/*/trade/jogos/swsh-lan.md`, a partir dos passos que o pokeldn testou num Sword real, num Switch 2 pelo
  Wi-Fi.
- **Rede.** O pokeldn usa UDP 30000 (busca), 40000 (broadcast) e 49152–49155 (estações), sempre na rede local.
  - A trava de rede (D21) deixa passar. A única consulta de nome do pokeldn (`gethostbyname_ex` do próprio
    computador) é recusada e ele segue sem ela.
  - O endereço vem da rota padrão: um "connect" UDP para 192.0.2.1, endereço reservado, sem enviar nada.
- **Firewall do Windows.** Tarefa nova do instalador, marcada por padrão: entrada UDP nessas portas, só no perfil
  "Privada" e só para o `esp32-runtime\python\python.exe`, que é quem roda o pokeldn. A desinstalação apaga a
  regra, e o `smoke-windows.ps1` confere as duas coisas.
- **Verificado:** testes do worker e da API (modo LAN sem configuração, sem código, recusa de outro jogo) e
  compilação do instalador pelo Inno Setup do Windows.
- **Não verificado:** a troca com um Sword/Shield de verdade e o `smoke-windows.ps1`, que pede o UAC.

## D25. Sem o motor do easyworld; teste do modo AP (Entrega 17)

O autor deixou as duas escolhas comigo ("pode seguir da forma que achar melhor").

- **O easyworld saiu.** O motor C# (`Esp32SerialEngine`, `SerialEsp32Link`, `Frlg.Trade.Core.Patched`), o patch
  `engines/patches/frlg-ldn-trade-esp32`, o ESP32 falso (`fake_esp32.py`) e o submódulo foram removidos. O motor
  já estava fora do registro desde a D23.
  - Os sprites 1–386 (do PokeAPI) e os dois Pokémon de demonstração (MEWTWO e DEOXYS, do autor do easyworld)
    foram copiados para `assets/sprites` e `assets/demo`. O `SHA256SUMS` dos sprites agora cobre 1–1025.
  - Sem o núcleo, o BouncyCastle e o ZstdSharp saem das dependências. O `System.IO.Ports` (portas COM no Windows)
    passa a ser referência direta do `TradeRock.Core`.
- **O FRLG com a placa Wi-Fi continua no frlg-ldn-trade**, em que o console abre a sala e a placa entra. Assim, uma
  placa sem modo AP ainda troca com o FRLG. Os jogos novos seguem pelo pokeldn (D23).
- **Teste do modo AP.** Nos jogos novos o TradeRock abre a sala, e a biblioteca LDN (`ldn.create_network`) cria a
  interface em modo AP com o monitor ao lado. O teste da placa (`card_child.py`) ganhou essa etapa:
  `Factory.create_ap` + `create_monitor` no primeiro canal que sintonizou, por meio segundo, com um SSID qualquer e
  sem chave (limite de 15 s).
  - O resultado vai à parte (`ap`, `apError`): sem AP a placa continua aprovada, porque troca com o FRLG.
  - Conexão mostra o aviso `card_test_no_ap`, que não bloqueia. Um teste de antes desta entrega (`ap` nulo) mostra
    `card_test_ap_unknown` e sugere testar de novo.
  - Com `ap = false`, o `PythonLdnEngine` recusa os jogos novos antes de chamar o worker (`card_no_ap`), sem pedir a
    senha à toa.
- **Catálogo de ESP32 com os dados do pokeldn** (`docs/hardware_esp32.md` do submódulo). O catálogo ainda descrevia
  o firmware do easyworld, e com o do pokeldn duas coisas mudam:
  - **S3 e C6 usam a USB nativa**, como o C3. Pela porta do conversor o firmware do pokeldn não responde. O
    catálogo, o guia e o aviso `port_should_be_native` foram corrigidos.
  - **O ESP32 clássico deixa de exigir o WROOM-32E.** O pokeldn relata uma placa clássica trocando com todos os
    jogos, e só um WROOM-32 genérico que não ouve o Switch. O WROOM-32 passa de "não suportado" para "sem teste".
    A confirmação do módulo (estado, caixa de seleção e desenho) saiu, e o aviso de revisão antiga do chip agora
    manda fazer o teste com o Switch.
  - Nenhuma placa fica como "recomendada": o TradeRock ainda não trocou com nenhuma.
- **Verificado:** build, testes do .NET, do worker e do frontend.
- **Não verificado:** a etapa do AP numa placa de verdade (precisa de root e de uma placa no Linux).

## D26. Outras atividades: Mystery Gift (Entrega 18a)

Respostas do autor: começar pelo Mystery Gift e fazer todas as atividades; o app confere com o próprio PKHeX, sem
o ajudante do pokeldn; nada gravado na placa; sem backup nem restauração do save; raids de evento embutidas (18b).

- **Tela "Atividades"** (`/atividades`, só na janela do PC, como a configuração do rádio). A pessoa escolhe o jogo
  (FRLG ou Sword/Shield, os que têm Mystery Gift local) e o presente:
  - **prontos do pokeldn:** no FRLG, Wonder Cards, mudanças num Pokémon, mudanças no jogo, mais presentes e Wonder
    News; no SwSh, Pokémon, itens e roupas;
  - **eventos oficiais do Sword/Shield:** os 189 cartões que o pokeldn traz (`pokeldn/swsh/data/events.json`,
    gerados por ele a partir da EventsGallery), sem internet;
  - **arquivo:** `.wc3` (FRLG), `.wc8` (SwSh) ou `.pokegift`, até 256 KiB.
- **Catálogo** em `catalog/gifts.json`, gerado do próprio pokeldn por `scripts/pokeldn/gift_catalog.py`. O
  `scripts/test-pokeldn.sh` confere se ainda bate (`UPDATE_CATALOG=1` regrava). Ficam de fora os Pokémon de evento do
  FRLG (pedem o ajudante PKHeX do pokeldn), a leitura do save, os presets com opções e as melhorias guardadas
  ("Game boosts"). Os nomes dos prontos e dos grupos têm tradução em `frontend/src/locales/gifts.pt-BR.json`,
  conferida por teste; os eventos oficiais ficam com o nome original.
- **Worker:** o `pokeldn_trade.py` ganhou o pedido `"activity": "gift"`. Ele roda `frlg_mg_host.py` ou
  `swsh_gift_host.py` com o valor do campo "builder" do pokeldn e chama `command.prepare`, que grava o
  `.pokegift`. O `gift_builder` troca uma chave desconhecida pela primeira da lista, então o worker confere a
  chave antes, com o mesmo filtro do catálogo. `--save-to-board` fica sempre desligado. O modo LAN não manda
  presentes.
- **Fim da atividade:**
  - **FRLG:** deu certo com `[done] delivery N complete`.
  - **Sword/Shield:** o console só ouve o anúncio e não confirma. Deu certo se o presente ficou no ar
    (`advertising comm id`). A pessoa encerra com "Terminei" depois de aceitar o cartão; senão, ele fica no ar
    por 5 minutos.
- **Patch:** `PkhexService.validate_gift` confere só o tamanho e a soma do cartão. O app confere o arquivo
  aberto (tamanho do `.wc3` e do `.wc8`, jogo do `.pokegift`).
- **Rádio:** uma atividade por vez, sem fila (`ActivityService`). Troca e atividade não rodam juntas: cada uma
  recusa com 409 enquanto a outra roda. Com a placa Wi-Fi, a atividade precisa do modo AP (D25), o FRLG
  inclusive. O modo demonstração simula os marcos.
- **Remetente:** o "Meu treinador" do jogo, ou o treinador padrão.
- **Correção junto:** a troca com o Legends: Arceus chamava o ajudante PKHeX do pokeldn por fora do
  `prepare_offer` e falhava sempre. O patch cobre também o `pokemon.validate`.
- **Verificado:** testes do worker (validação, comando de cada presente, marcos), da API (catálogo, presente
  simulado, rádio ocupado, pedido ao pokeldn pelo ESP32) e das traduções.
- **Não verificado:** a entrega num console de verdade e a tela num navegador.

## D27. Modo servidor com os jogos novos (Entrega 19)

- **Já funcionava.** A troca pela fila, o cofre por conta e o "Meu treinador" usam o jogo desde a Entrega 14: o
  seletor de jogo é a mesma tela no celular, a fila guarda o pedido inteiro (jogo, código, trocar de volta) e o
  "Meu treinador" de cada jogo fica nas configurações da conta (`UserSettings`).
- **Teste novo** (`ServerModeTests.Conta_pela_rede_troca_com_o_scarlet_com_o_proprio_treinador`): uma conta pela
  rede envia um .pk9, grava o próprio treinador do SV, troca com o Scarlet pela fila e recebe no próprio cofre; a
  outra conta não vê o recebido nem o treinador.
- **Outras atividades só na janela do PC** (`/api/activities` em `Caller.IsLocalOnly`). Elas não passam pela fila:
  quem está no PC escolhe o presente ou a raid e acompanha. Liberar pela rede pediria uma fila de atividades; fica
  para quando o autor quiser.
- **Não verificado:** com um celular de verdade (como a Entrega 10).

### D26 (continuação). Raids (Entrega 18b)

- **Tipos:** Max Raid e Dynamax Adventure no Sword/Shield, Tera Raid no Scarlet/Violet. Em cada um, o console entra
  na sala do TradeRock (host) ou o TradeRock entra na do console (join). As do Sword/Shield também vão pela rede de
  casa: o motor `lan` usa as ferramentas `swsh-lan-*` do pokeldn, sem rádio nem chaves.
- **Pokémon do cofre:** o chefe da Max Raid montada, o que o TradeRock leva e o chefe da Tera Raid, no formato do
  jogo (`PokeldnOffer`).
- **Raids de evento, embutidas.** A EventsGallery do Project Pokémon foi baixada no desenvolvimento por
  `scripts/fetch-raid-events.sh`, com o próprio pokeldn, em commits travados: SV `bc61f78` e SwSh `0595d45`.
  - Fica num .zip determinístico de 5 MB (`assets/raid-events`, conferido pelo SHA256SUMS). O app não baixa nada
    (D17). A galeria não declara licença; são dados dos eventos oficiais, a mesma situação dos sprites.
  - O `stage-pokeldn.sh` leva o .zip. O worker extrai só o evento escolhido na pasta da sessão, com o caminho longo
    no Windows (os nomes passam de 150 caracteres), e aponta o `events_gallery.location` do pokeldn para lá.
  - A tela usa `catalog/raids.json` (eventos, chefes do SwSh, grupos e etapas do SV), gerado por
    `scripts/pokeldn/raid_catalog.py` e conferido no `scripts/test-pokeldn.sh` (`UPDATE_CATALOG=1` regrava). O app
    confere a combinação escolhida antes de chamar o pokeldn.
- **Sem o ajudante PKHeX do pokeldn:**
  - os prêmios escolhidos (`--raid-reward`) ficam de fora, e a raid dá os prêmios da toca, do evento ou da semente;
  - o patch passa a cobrir também o `pokemon.prepare`, que o `swsh_connect.py` (entrar na sala do console) chama
    sempre.
- **Fim:**
  - o pokeldn não diz quem venceu, e cada console luta a própria batalha. A raid conta como concluída quando a
    batalha começa ("lobby", "battle" e "over" vêm das linhas dos scripts);
  - a Max Raid hospedada fica aberta até a pessoa clicar em Terminei, ou até o tempo do pokeldn acabar;
  - as outras saem sozinhas.
- **Verificado:** testes do worker (validação, comando de cada tipo, evento extraído do .zip), da API (raid simulada,
  conferências, ESP32 e rede de casa) e o typecheck da tela.
- **Não verificado:** raid com console de verdade.

### D26 (continuação). Union Circle (Entrega 18c)

- **Só no Scarlet/Violet, e o console é sempre o anfitrião:** ele abre um grupo local com um código de 4 dígitos, e o
  TradeRock entra pelo `sv_join.py` do pokeldn.
  - **Missões do Blueberry** (`sv-circle-join`): todos os lendários do Snacksworth, ou os BP escolhidos
    (1 a 9.999.999). Dá certo quando o pokeldn diz que a tarefa terminou.
  - **Encontros** (`sv-circle-encounters-join`): espécie (as que existem no SV), nível, tipo Tera e shiny. Ficam até
    o console fechar o círculo. Dá certo quando o círculo começou.
- **Sem o "só encontros legais"** (`--spawn-legal`), que pede o ajudante PKHeX do pokeldn.
- **Marcos novos:** "lobby" quando o console aceita a entrada (também na Tera Raid em que o TradeRock entra),
  "battle" quando o círculo começa e "over" quando a tarefa termina.
- **Verificado:** testes do worker (validação, comando, marcos) e da API (simulado, conferências, pedido pelo ESP32).
- **Não verificado:** com um Scarlet/Violet de verdade.
