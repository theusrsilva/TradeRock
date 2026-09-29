# Especificação original

Texto enviado pelo autor no início do projeto, sem edição. O nome mudou depois para **TradeRock**, e
algumas decisões mudaram o que está aqui (.NET 10 em vez de 8, submódulo em vez de fork, worker em
Python, stdin/stdout em vez de socket Unix etc.): veja `CLAUDE.md` e `docs/decisions.md`.

---

# Projeto: FRLG Trade Studio (aplicativo desktop)

Quero construir um aplicativo desktop para trocar Pokémon com Pokémon FireRed e LeafGreen do
Nintendo Switch / Switch 2 (relançamentos da eShop, 2026) via wireless local (LDN). O foco é
EXCLUSIVAMENTE FRLG do Switch. Estilo pkvault/pkforge, mas dedicado a esse fluxo: entender e
validar os requisitos, montar o Pokémon, validar a legalidade com o PKHeX, trocar e guardar os
recebidos num cofre. O público inclui pessoas leigas, então tudo precisa ser explicado de forma
simples.

## Plataformas e distribuição
- Fase 1: Linux (x64), como AppImage e .deb. Motor PRIORITÁRIO: a placa Wi-Fi do próprio PC (Linux
  nativo, não WSL), porque assim o usuário talvez não precise comprar nada. Alternativa: ESP32 via
  USB, para quem não tem placa compatível ou prefere não mexer na rede.
- Fase 2: Windows (x64), com instalador (Inno Setup ou MSIX). Só o motor ESP32, porque o Windows
  não dá o acesso ao Wi-Fi que o LDN exige.
- Um só código para as duas plataformas, com as diferenças isoladas em implementações por
  plataforma.

## Base técnica (leia o código antes de implementar)
- Motor Python: https://github.com/tornadus/frlg-ldn-trade (Linux, Python 3.12+, root, placa Wi-Fi
  que transmita action frames em modo monitor, prod.keys).
  `sudo -E ./venv/bin/python frlgtrade.py --live -o output.pk3 PARTY1.pk3 PARTY2.pk3`
  Flags: --verbose, --phy, --keys, --ot, --version (firered|leafgreen), --id (TID ou TID:SID).
  Fluxo no jogo: o console vira "Leader" no Direct Corner, aprova o join de "EMU", o jogador anda
  até a cadeira da esquerda e troca. O Switch recebe o PARTY2 e o Pokémon enviado sai em output.pk3.
  A conexão pode precisar de várias tentativas.
- Fork com mais opções de treinador (Pokédex Nacional, idioma etc.):
  https://github.com/MercuryEnigma/frlg-ldn-trade. Avalie se vale usar como base.
- Wiki do protocolo: https://github.com/tornadus/frlg-ldn-trade/wiki
- Biblioteca LDN: https://github.com/kinnay/LDN
- Port em C# + firmware ESP32: https://github.com/easyworld/frlg-ldn-trade-esp32
  (host/core em C# puro: LDN, serial, Pia, RFU, máquina de estados da troca).
- Outra implementação com ESP32: https://github.com/GB-Link/GB-Link-Switch-LDN
- Referência de UX e teste de placa: https://github.com/ImaEternal/FRLG-LDN-Trade-Console
- Decomp: https://github.com/pret/pokefirered (fonte da verdade para regras do jogo)
- PKHeX.Core (NuGet): parse, LegalityAnalysis, encounters.
- PKHeX-Plugins / PKHeX.Core.AutoMod: gerar Pokémon legais a partir de sets e de texto Showdown.

## Arquitetura
- Um único processo .NET 8 self-contained (sem exigir .NET instalado):
  - ASP.NET Core embutido, servindo a API REST + WebSocket/SSE e o frontend.
  - Janela nativa com Photino.NET (WebKitGTK no Linux, WebView2 no Windows), apontando para o
    servidor local em 127.0.0.1, porta aleatória, com token de sessão para que outros processos da
    máquina não acessem a API.
- Frontend: React + Vite + TypeScript + Tailwind, compilado e embutido no executável.
- Motor de troca plugável (ITradeEngine):
  1. `PythonLdnEngine` (só Linux, motor padrão no Linux): executa o frlg-ldn-trade com a placa
     Wi-Fi do PC, com um runtime Python embutido (python-build-standalone) e as dependências já
     instaladas.
  2. `Esp32SerialEngine` (Linux e Windows; alternativa no Linux, único no Windows): usa o host/core
     em C# do easyworld com o ESP32 no USB. Inclui a gravação do firmware pelo próprio app
     (esptool embutido).
  3. `FakeEngine`: simula a troca completa com eventos e atrasos realistas, sem Switch, placa ou
     prod.keys. Padrão em modo desenvolvimento.
- Worker privilegiado (só Linux e só para o PythonLdnEngine): executável separado, iniciado via
  pkexec com uma política polkit própria (instalada pelo .deb, ou pelo app na primeira execução do
  AppImage, com confirmação). O app principal NUNCA roda como root e fala com o worker por um
  socket Unix com uma API mínima (iniciar troca, cancelar, status, testes de placa). O app nunca
  pede nem guarda senha. O motor ESP32 não precisa de worker.
- Eventos estruturados do motor: se necessário, faça um fork mínimo com `--json-events` (uma linha
  JSON por evento: connecting, beacon_found, beacon_decrypted, join_requested, joined, in_room,
  trade_offered, trade_done, error). Senão, parseie a saída do --verbose.
- Persistência: SQLite (EF Core) na pasta de dados do usuário (Linux: ~/.local/share/frlg-trade-studio;
  Windows: %APPDATA%\FrlgTradeStudio). Os .pk3 ficam em disco com hash SHA-256.
- Motores externos entram como submódulos git, sem copiar código.

## Catálogo de hardware (dados, não código)
Toda informação de compatibilidade fica em `hardware-catalog.json`, versionado e embutido no app,
com opção de atualizar pelo GitHub Releases. Cada entrada tem: modelo, tipo, chip/driver, status
(recomendado / funciona / instável / problemático / não suportado), observações em português,
fonte (link do repositório de origem) e data da última verificação. A interface mostra a data e
avisa que a compatibilidade muda rápido. Drivers desconhecidos (fora do catálogo) são tratados
como "não testado": o app deixa testar, sem prometer nada.

Popule a versão inicial a partir dos repositórios originais, conferindo cada dado na fonte (não
invente modelos nem status). O que eu já sei, para você confirmar:
- Placas Wi-Fi (Linux):
  - alta confiabilidade: ALFA AWUS036ACHM (USB, mt76x0u), Realtek RTL8821CE (interna, rtw88_8821ce)
  - relatada como compatível num PR: Realtek RTL8822CE (rtw88_8822ce)
  - baixa confiabilidade: AMD RZ616 (mt7921e)
  - problemáticas: Intel AX200 (iwlwifi) e Atheros AR9271 (ath9k_htc), que não recebem IP
- ESP32:
  - ESP32-S3: o mais testado, com trocas completas relatadas. Recomendado.
  - ESP32-C6: trocou em revisão anterior, com recepção mais fraca que o S3.
  - ESP32-C3: compila, mas sem troca confirmada.
  - ESP32 clássico: exige o módulo ESP32-WROOM-32E. O WROOM-32 antigo tem problemas conhecidos e
    não é suportado. Explicar ao usuário como conferir o modelo na blindagem metálica do módulo.
  - O firmware depende de uma versão fixa do ESP-IDF: o app grava a versão que ele traz embutida.

## Módulo 1: Assistente de primeira execução (explicar, preparar, validar)
Um assistente em etapas, com barra de progresso, que também fica acessível depois em
Configurações. Tom didático, frases curtas, ilustrações e seções "Saiba mais" expansíveis para quem
quer detalhes técnicos. Cada verificação mostra status (ok / aviso / erro), explicação e botão
"como resolver" ou "corrigir". A tela de troca fica bloqueada até tudo o que é essencial passar.

### Etapa 1: Boas-vindas e visão geral
- O que o app faz, em linguagem simples: o PC (ou o ESP32) finge ser um segundo Switch na sala de
  troca do jogo.
- Checklist do que a pessoa vai precisar, adaptado ao sistema detectado:
  - Todos: um Switch ou Switch 2 com FireRed/LeafGreen jogado até liberar o Direct Corner
    (cerca de 20 a 40 minutos), e o arquivo prod.keys.
  - Linux: uma placa Wi-Fi compatível (talvez o seu PC já tenha) OU um ESP32 compatível.
  - Windows: um ESP32 compatível (a placa Wi-Fi do PC não serve no Windows).
- No Linux, já nesta etapa, rodar uma detecção rápida da placa Wi-Fi em segundo plano e mostrar
  uma prévia: "Seu PC tem a placa X, que é compatível" ou "Sua placa não está na lista de
  compatíveis; você ainda pode testar ou usar um ESP32".
- Tempo estimado de configuração.

### Etapa 2: O que é o prod.keys
- Explicação simples: um arquivo com as chaves criptográficas do Switch. O wireless local do jogo é
  criptografado, e sem essas chaves o app não consegue conversar com o Switch.
- Como se obtém: extraindo do próprio console, com o Lockpick_RCM, num Switch 1 com CFW (modelos
  vulneráveis ou com modchip). O Switch 2 não permite extrair as chaves.
- Aviso claro: as chaves são material protegido da Nintendo. Só use as extraídas do seu próprio
  console. Não baixe da internet nem compartilhe. Parte delas é única do seu aparelho.
- O app nunca baixa chaves nem sugere fontes.
- Upload e validação:
  - Arquivo salvo com permissão 600 (no Windows, ACL só do usuário) e lido só pelo motor/worker.
  - Validação de formato: legível e com as chaves que a biblioteca LDN realmente usa (confira no
    código do kinnay/LDN, não invente nomes). A interface deixa claro que isso NÃO prova que os
    valores estão corretos; a prova vem no teste com o Switch (etapa 6).
  - NUNCA exibir valores, logar, enviar pela rede, incluir em backups, exports ou relatórios.
- Se a pessoa ainda não tem o arquivo: permitir pular e explorar o app (construtor, cofre, modo
  demonstração com FakeEngine), com a troca bloqueada.

### Etapa 3: Escolher o "rádio" (explicação por sistema)
Mostrar apenas as opções válidas para o sistema detectado.

Linux (a placa Wi-Fi vem primeiro e pré-selecionada quando fizer sentido):
- Lógica de recomendação, baseada na detecção:
  - Placa compatível detectada (status recomendado/funciona): recomendar a placa do PC, com o
    destaque "você não precisa comprar nada". O ESP32 aparece abaixo como alternativa.
  - Placa instável ou não testada: recomendar testar a placa primeiro, explicando que pode
    funcionar mal, e mostrar o ESP32 como plano B.
  - Placa problemática, nenhuma placa, WSL ou máquina virtual: recomendar o ESP32, explicando o
    motivo, e manter a opção da placa visível mas desabilitada, com a explicação.
- Opção A, placa Wi-Fi do PC (recomendada):
  - Vantagem: sem custo, se a placa for compatível.
  - Requisitos: Linux instalado de verdade (não WSL, não máquina virtual), placa compatível,
    permissão de administrador uma vez para instalar o componente de rede.
  - A placa fica ocupada durante as trocas: se ela for a internet do PC, a conexão cai durante a
    troca. Solução: cabo de rede ou um segundo adaptador.
  - Mostrar a tabela de placas do catálogo, com a placa detectada destacada.
  - Se a placa interna não servir, sugerir antes um adaptador USB Wi-Fi compatível do catálogo
    (ex.: ALFA AWUS036ACHM) como opção, ao lado do ESP32, com prós e contras de cada um.
- Opção B, ESP32 via USB (alternativa):
  - Vantagem: funciona com qualquer placa de rede do PC, não precisa de administrador para trocar e
    não derruba a internet.
  - Requisitos: um ESP32 compatível (tabela do catálogo, com o S3 como recomendado), cabo USB de
    DADOS (muitos cabos só carregam), permissão na porta serial.

Windows:
- Só o ESP32, com a explicação de por que a placa Wi-Fi do PC não serve (o Windows não permite o
  acesso de baixo nível ao Wi-Fi que o LDN exige).
- Requisitos: ESP32 compatível, cabo USB de dados e, se a placa usar chip USB-serial (CH340,
  CP210x), o driver correspondente, com link para a página oficial do fabricante.
- Deixar claro que NÃO é preciso nenhuma placa de rede especial no PC.

Em ambos: um guia "qual ESP32 comprar", com fotos, como identificar o modelo e observações do
catálogo, sem links de lojas.

### Etapa 4: Preparar o rádio escolhido

Placa Wi-Fi (Linux):
1. Sistema: Linux nativo. Se detectar WSL (/proc/version contém "microsoft") ou máquina virtual,
   erro explicando o motivo e sugerindo o ESP32.
2. Worker privilegiado e política polkit instalados e respondendo.
3. Listar /sys/class/ieee80211/phy*, o driver de cada uma e cruzar com o catálogo. Escolher o phy
   (vira --phy).
4. Teste real: modo monitor + transmissão de action frames. É este teste que diz se a placa está
   válida e acessível; o driver sozinho não basta.
5. Se o teste falhar: explicar o que falhou e oferecer, nesta ordem, tentar de novo, escolher outro
   phy, ou trocar para o ESP32 sem perder o que já foi configurado.
6. Avisar se o phy escolhido é a conexão de internet do PC.
7. NetworkManager: oferecer instalar /etc/NetworkManager/conf.d/zz-ldn-unmanaged.conf com
   `unmanaged-devices=interface-name:ldnclient;interface-name:ldn;interface-name:ldn-mon;interface-name:ldn-tap`
   e reiniciar o serviço, sempre com confirmação.

ESP32 (Linux e Windows):
1. Detectar portas seriais e identificar o chip conectado (via esptool) e o módulo, quando
   possível. Cruzar com o catálogo. Se for o ESP32 clássico, pedir para o usuário confirmar o
   módulo WROOM-32E.
2. Linux: checar se o usuário está no grupo dialout (Debian/Ubuntu) ou uucp (Arch) e mostrar o
   comando para corrigir, avisando que é preciso sair e entrar da sessão.
3. Linux: detectar o brltty ativo, que costuma sequestrar adaptadores CH340/CP210x, e sugerir
   desativá-lo.
4. Linux: oferecer uma regra udev com nome fixo para a porta (ex.: /dev/frlg-esp32).
5. Windows: detectar se falta o driver do chip USB-serial e orientar a instalação.
6. Ler a versão do firmware e oferecer gravar a versão embutida no app, com barra de progresso e
   instruções para o modo de gravação (botão BOOT) quando necessário.
7. Teste de comunicação: o firmware responde pela serial.

### Etapa 5: Preparar o jogo
Instruções ilustradas: jogar até o Direct Corner ficar disponível, 2º andar do Pokémon Center,
como abrir a sala de troca como Leader.

### Etapa 6: Teste com o Switch
O usuário abre o Direct Corner como Leader. O app procura o beacon da sala e tenta
descriptografá-lo, sem entrar na sala. Resultados:
- "Beacon encontrado e descriptografado": rádio e chaves confirmados. Troca liberada.
- "Beacon encontrado, mas não descriptografado": chaves incorretas.
- "Nenhum beacon encontrado": Switch longe, sala não aberta ou problema no rádio, com dicas.

### Etapa 7: Resumo
Tudo o que foi configurado, com botão de exportar um relatório para pedir ajuda em fóruns (sem
dados sensíveis), botão para refazer qualquer etapa e botão para trocar de motor a qualquer momento.

## Módulo 2: Construtor de Pokémon (legal por padrão)
- Formulário: espécie (#1–386, com sprites), apelido, nível, natureza, habilidade, IVs, EVs,
  4 golpes, item, pokébola, shiny, gênero, OT, TID/SID, idioma, local e versão de encontro.
- Geração via AutoLegalityMod: buscar um encounter válido e gerar o PID/IV correto. Lembre que na
  Gen 3 natureza, habilidade, gênero e shiny derivam do PID, e que o PKHeX checa a correlação
  PID-IV (Method 1/2/4 etc.).
- Import: .pk3/.ek3, texto Showdown e extração de um save .sav de GBA.
- Legalidade em tempo real (LegalityAnalysis), com cada verificação explicada em português, o
  campo destacado e correção automática quando possível.
- Três estados: Legal / Ilegal / Não verificável. Distribuições novas da versão de Switch (ex.:
  Lugia, Ho-Oh, Celebi) podem não estar na base do PKHeX: nesse caso, "não verificável", nunca "legal".
- Regras do jogo que afetam a troca: confira na decomp se o FRLG recusa Pokémon de fora de Kanto
  sem a Pokédex Nacional ou outros requisitos de progresso, e mostre avisos.
- Pokémon ilegal fica bloqueado para troca por padrão, com override explícito em modo avançado.
- Todo Pokémon é salvo no cofre antes de qualquer troca.

## Módulo 3: Assistente de troca
1. Escolher o Pokémon que vai para o Switch (PARTY2) e um de preenchimento (PARTY1), do cofre.
2. Escolher FireRed/LeafGreen (--version) e o treinador simulado (--ot, --id), com presets.
3. No modo servidor: entrar na fila e aguardar a vez.
4. Lembrete ilustrado: Direct Corner → ser Leader.
5. Iniciar o motor com progresso ao vivo e retry automático configurável (padrão 5 tentativas).
6. Guiar: aprovar "EMU" → cadeira da esquerda → escolher → confirmar → cancelar → sair da sala.
7. Ao terminar: importar o output.pk3 para o cofre, rodar a legalidade e mostrar o card do recebido.
8. Timeout, cancelamento seguro (encerra o motor e restaura o estado do rádio) e liberação da fila.
9. Fechar o app durante uma troca pede confirmação e faz o mesmo encerramento seguro.
10. Histórico: data, enviado, recebido, jogo e log sem chaves.

## Módulo 4: Cofre
- Caixas estilo pkvault, busca, filtros (espécie, shiny, legalidade, origem) e tags.
- Arrastar e soltar .pk3/.ek3 na janela para importar.
- Export .pk3/.ek3 e injeção num .sav de GBA, com backup automático do .sav original.
- Detecção de duplicatas por hash.

## Modo servidor na rede (opcional, desligado por padrão)
- Um interruptor nas configurações faz o app servir a mesma interface para a rede local enquanto
  estiver aberto.
- Ao ligar: escolher a interface de rede (NUNCA a placa Wi-Fi usada no LDN; com o motor Wi-Fi no
  Linux, exigir cabo ou um segundo adaptador; com ESP32, qualquer interface serve), criar contas
  locais (hash Argon2, papéis admin e usuário, cofre por usuário), rate limiting, proteção CSRF e
  aviso contra expor na internet.
- Mostrar IP, porta e QR code para abrir no celular.
- Um rádio, uma troca por vez: fila com posição visível para todos.

## Ambiente de desenvolvimento
- Devcontainer (VS Code / Dev Containers) com .NET 8 SDK, Node LTS, Python 3.12, esptool e as
  dependências de build do AppImage e do .deb. O mesmo Dockerfile é usado no CI.
- docker-compose.dev.yml com backend e frontend em hot reload, usando o FakeEngine por padrão.
- O PythonLdnEngine com a placa Wi-Fi real NÃO roda no container. Como ele é o motor prioritário,
  prepare desde cedo um fluxo de teste no host: script que compila o app e o worker, instala a
  política polkit de desenvolvimento e roda com a placa real. Documente o motivo (acesso ao phy,
  NetworkManager, root).
- ESP32 dentro do container: repassar a porta com `--device` (documentar no README).
- Docker é só para desenvolvimento e build. O produto final é AppImage/.deb (e instalador no
  Windows), nunca uma imagem Docker.

## Requisitos não funcionais
- Interface em português do Brasil, i18n preparado para inglês, modo claro e escuro, responsiva.
- Todos os textos explicativos do assistente ficam em arquivos de conteúdo (markdown por idioma),
  fáceis de revisar sem mexer em código.
- Logs estruturados com rotação, mascarando dados de chave, com botão "abrir pasta de logs".
- Verificação de atualizações do app e do catálogo pelo GitHub Releases (opcional, desligável),
  sem telemetria.
- Testes: unitários para o validador, o catálogo, a lógica de recomendação, o parser de eventos e a
  fila; ponta a ponta com o FakeEngine; testes do assistente simulando Linux com placa compatível,
  Linux com placa instável, Linux com placa desconhecida, Linux com placa problemática, WSL,
  máquina virtual, Linux com ESP32 e Windows com ESP32.
- CI no GitHub Actions: AppImage e .deb na fase 1; instalador Windows na fase 2.
- Licenças: frlg-ldn-trade é AGPL-3.0 e PKHeX.Core é GPL-3.0; verifique as do port em C#, do
  firmware ESP32 e do Photino. O projeto deve ser AGPL-3.0, com tela "Sobre" dando os créditos.
  Nunca distribuir prod.keys nem ROMs.
- README com instalação, diagrama da arquitetura e troubleshooting de placas e do ESP32.

## Entregas, nesta ordem
1. Monorepo (app/, worker/, frontend/, engines/ como submódulos, firmware/, packaging/, content/,
   docs/), devcontainer e janela Photino abrindo o frontend.
2. FakeEngine + assistente de troca de ponta a ponta sem hardware.
3. Construtor + legalidade.
4. Cofre.
5. Catálogo de hardware + assistente de primeira execução completo (explicações, lógica de
   recomendação e validações), com detecção de sistema e simulação dos cenários.
6. Worker privilegiado + polkit + PythonLdnEngine com runtime Python embutido + fluxo de teste
   no host.
7. Teste do beacon com a placa Wi-Fi.
8. Empacotamento Linux (AppImage e .deb) e CI.
9. Esp32SerialEngine + gravação de firmware + validações do ESP32 no Linux + teste do beacon
   com o ESP32.
10. Modo servidor na rede.
11. Build e instalador Windows (só ESP32).
Antes de cada etapa, apresente o plano e as dúvidas. Não invente APIs do PKHeX, da biblioteca LDN,
do port em C#, do firmware ou do Photino: consulte o código-fonte e, se algo não existir, me diga.
