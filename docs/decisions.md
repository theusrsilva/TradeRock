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
