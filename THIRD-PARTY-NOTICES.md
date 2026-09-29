# Componentes de terceiros

O TradeRock é distribuído sob a GNU AGPL-3.0. Ele usa os projetos abaixo, cujas licenças são
compatíveis com a AGPL-3.0. Licenças conferidas no código-fonte em 2026-09-29.

| Projeto | Uso no TradeRock | Licença |
| --- | --- | --- |
| [tornadus/frlg-ldn-trade](https://github.com/tornadus/frlg-ldn-trade) | Motor de troca com a placa Wi-Fi (submódulo) | AGPL-3.0 |
| [kinnay/LDN](https://github.com/kinnay/LDN) | Biblioteca LDN usada pelo motor Python | GPL-3.0 |
| [easyworld/frlg-ldn-trade-esp32](https://github.com/easyworld/frlg-ldn-trade-esp32) | Core C# e firmware ESP32 (submódulo) | AGPL-3.0 (componente LDN: GPL-3.0) |
| [PKHeX.Core](https://github.com/kwsch/PKHeX) | Leitura e legalidade de Pokémon (NuGet) | GPL-3.0 |
| [santacrab2/PKHeX-Plugins](https://github.com/santacrab2/PKHeX-Plugins) | PKHeX.Core.AutoMod: geração de Pokémon legais (submódulo) | MIT |
| [Photino.NET](https://github.com/tryphotino/photino.NET) | Janela nativa (NuGet) | Apache-2.0 |
| [pret/pokefirered](https://github.com/pret/pokefirered) | Referência das regras do jogo (não distribuído) | sem código incorporado |
| [ImaEternal/FRLG-LDN-Trade-Console](https://github.com/ImaEternal/FRLG-LDN-Trade-Console) | Os dois patches do motor em `engines/patches/frlg-ldn-trade` (sem alteração) e referência de UX/diagnóstico | Patches: AGPL-3.0 (mudam código AGPL); o resto do projeto é EUPL-1.2 e não foi incorporado |
| [python-build-standalone](https://github.com/astral-sh/python-build-standalone) | CPython 3.12 embutido no worker | Python: PSF-2.0; bibliotecas incluídas com as próprias licenças (ver a pasta `python/` do worker) |
| [python-netlink](https://pypi.org/project/python-netlink/) | Dependência da biblioteca LDN | GPL-3.0 |
| [trio](https://github.com/python-trio/trio), outcome, sniffio | Dependências da biblioteca LDN | MIT ou Apache-2.0 |
| attrs, idna, sortedcontainers | Dependências do trio | MIT; BSD-3-Clause; Apache-2.0 |
| [pycryptodome](https://github.com/Legrandin/pycryptodome) | Criptografia do motor e da biblioteca LDN | BSD-2-Clause e domínio público |
| [zstandard](https://github.com/indygreg/python-zstandard) | Compressão das mensagens do jogo (motor) | BSD-3-Clause |
| [iw](https://git.kernel.org/pub/scm/linux/kernel/git/jberg/iw.git) 6.17 | Binário estático embutido no worker (`bin/iw`) | ISC |
| [iproute2](https://git.kernel.org/pub/scm/network/iproute2/iproute2.git) 7.2.0 | Binário `ip` estático embutido no worker (`bin/ip`) | GPL-2.0 (programa separado; fonte oficial em `scripts/build-nettools.sh`) |
| [libnl](https://github.com/thom311/libnl) 3.12.0 | Ligada estaticamente ao `iw` embutido | LGPL-2.1 |
| [musl](https://musl.libc.org/) (via [Zig](https://ziglang.org/) 0.16.0) | libc estática do `iw`/`ip` embutidos; o Zig só compila, não é distribuído | MIT |
| [AppImage/type2-runtime](https://github.com/AppImage/type2-runtime) | Runtime no início do AppImage (monta e roda o app) | MIT; ligado estaticamente a squashfuse (BSD-2-Clause), libfuse 3 (LGPL-2.1), zstd e zlib (BSD/zlib). Fonte no repositório indicado |
| [GB-Link/GB-Link-Switch-LDN](https://github.com/GB-Link/GB-Link-Switch-LDN) | Referência (não distribuído) | AGPL-3.0, sem código incorporado |
| [PokeAPI/sprites](https://github.com/PokeAPI/sprites) | Sprites 1–386 (via `assets/sprites` do easyworld) | Arte © Nintendo, Game Freak e The Pokémon Company; uso não autorizado por eles |
| Pokémon de demonstração | `assets/party/mewtwo.pk3` e `deoxys.pk3` do easyworld | Fornecidos pelo autor do easyworld |

O TradeRock nunca distribui `prod.keys`, chaves do Switch nem ROMs.
