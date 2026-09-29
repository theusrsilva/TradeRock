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
