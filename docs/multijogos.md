# Troca com todos os jogos Pokémon do Switch 1 e 2

Plano aprovado pelo autor em 30/10/2026. Base técnica: o [pokeldn](https://github.com/Decryptu/pokeldn)
(AGPL-3.0, Python), um fork do frlg-ldn-trade que já fala o protocolo local (LDN + Pia) de cada jogo
e foi verificado em console de verdade. As decisões técnicas ficam na D21 de [decisions.md](decisions.md).

## Jogos

| Jogo | Formato (PKHeX) | Troca local no pokeldn | Extras do pokeldn |
| --- | --- | --- | --- |
| FireRed/LeafGreen | PK3 | ✓ | Mystery Gift, Union Room, batalha |
| Let's Go Pikachu/Eevee | PB7 | ✓ | evento por troca |
| Sword/Shield | PK8 | ✓ (e modo LAN, sem placa) | Mystery Gift, Max Raid, Dynamax Adventure |
| Brilliant Diamond/Shining Pearl | PB8 | ✓ | evento por troca |
| Legends: Arceus | PA8 | ✓ | evento por troca |
| Scarlet/Violet | PK9 | ✓ (código de troca) | Tera Raid, Union Circle, "Mystery Gift" simulado por troca |
| Legends: Z-A | PA9 | ✓ (código de troca) | evento por troca |

"Switch 2" são esses mesmos jogos rodando no console novo (inclusive o Z-A Switch 2 Edition). Não há
hoje jogo Pokémon exclusivo do Switch 2 com troca local suportado pelo pokeldn (Champions e Pokopia
não aparecem nele). O PKHeX.Core 26.8.26 já lê todos esses saves (SAV7b, SAV8SWSH, SAV8BS, SAV8LA,
SAV9SV, SAV9ZA).

## Respostas do autor

1. **pokeldn como motor:** pode, como submódulo (D2), com ajustes em `engines/patches/pokeldn`.
2. **Escopo:** tudo o que o pokeldn faz pela rede local: troca, raids, Mystery Gift, Union Circle e
   eventos. Nada do lado online dele (GTS, loja de plugins, atualizações): a D17 continua valendo.
3. **ESP32:** decisão deixada ao Claude, ver abaixo.
4. **Windows:** pode embutir o Python.
5. **Modo LAN sem placa:** entra. Só existe no Sword/Shield: a troca do Scarlet/Violet nunca usa LAN
   (levantamento da Entrega 13, D21).
6. **Legalidade:** o PKHeX confere e avisa, mas **não bloqueia** a troca.
7. **Cofre:** caixa única, com filtro de compatibilidade pelo jogo escolhido.
8. **Testes reais:** o autor tem todos os jogos.

### ESP32: um firmware só (o do pokeldn)

O firmware do easyworld só serve para o FRLG. Com dois firmwares, a pessoa teria que regravar a placa
toda vez que mudasse de jogo, o que não dá para pedir a um leigo. O do pokeldn atende todos os jogos,
inclusive o FRLG, nas mesmas placas (ESP32 clássico, S3, C3, C6). Por isso a placa passa a receber o
firmware do pokeldn. O motor do easyworld (C#) saiu do código na Entrega 17 (D25).

## Experiência de uso

A troca passa a começar por **"Com qual jogo você vai trocar?"** (cartões com a arte de cada jogo).
Depois da escolha:

- instruções do próprio jogo (onde fica a troca, se precisa de código, quem cria a sala), em
  `content/{pt-BR,en}/jogos/<jogo>/`;
- o cofre mostra só os Pokémon compatíveis com o jogo (formato e espécie existentes nele); os outros
  aparecem esmaecidos com o motivo;
- o PKHeX confere a legalidade para aquele jogo e mostra o aviso, mas deixa trocar;
- o rádio é o que estiver configurado (placa Wi-Fi no Linux, ESP32) ou, no Sword/Shield, a rede de
  casa (modo LAN).

Raids, Mystery Gift e Union Circle aparecem como "Outras atividades" no mesmo seletor de jogo, só nos
jogos que têm.

## Entregas

| # | Entrega | Conteúdo |
| --- | --- | --- |
| 13 ✓ | Integração do pokeldn | Submódulo travado num commit, patch do modo TradeRock, trava de rede, pokeldn no build do worker, testes offline do pokeldn no `ci.sh`, levantamento por jogo, D21. O Python embutido fica no 3.12 (D21) |
| 14 ✓ | Base multijogo | Catálogo de jogos; seletor de jogo na troca; cofre com formato por Pokémon e filtro; "Meu treinador" por jogo; construtor e legalidade por jogo (avisa sem bloquear); mover entre jogos como o HOME; sprites até a Gen 9; instruções por jogo (D22) |
| 15 ✓ | Motor pokeldn: ESP32 (Linux e Windows, firmware do pokeldn; D23) e placa Wi-Fi (Linux) | O TradeRock é o anfitrião; scripts de anfitrião do pokeldn com as flags do catálogo dele; ESP32 sem root (todos os jogos) e placa Wi-Fi pelo worker (jogos novos); trocar de volta (D23) |
| 16 ✓ | Modo LAN sem placa | Sword/Shield pela rede de casa, no Linux e no Windows, sem root |
| 17 ✓ | Limpeza do motor do easyworld (o ESP32 e o Windows foram adiantados na 15) | Motor C# e submódulo do easyworld fora; catálogo de ESP32 com os dados do pokeldn; teste da placa Wi-Fi em modo AP (D25) |
| 18 | Outras atividades (D26) | 18a ✓ Mystery Gift (FRLG e SwSh); 18b raids (SwSh e SV, com as raids de evento embutidas); 18c Union Circle |
| 19 ✓ | Modo servidor com os jogos novos | Fila e contas com o seletor de jogo (já funcionavam desde a 14; teste de ponta a ponta e D27) |

## Riscos

- O pokeldn muda todo dia e não tem API estável: travar o commit, cobrir o adaptador com testes e
  atualizar de propósito, não sozinho.
- O caminho da placa Wi-Fi no Linux é chamado de "legacy" pelo pokeldn. Todos os jogos aceitam a
  placa no código, mas o caminho mais testado por ele é o ESP32.
- O app do pokeldn tem partes online (GTS, plugins, atualizações, sprites). O patch 0001 desliga essas
  partes e a trava de rede do worker recusa qualquer conexão fora da rede local (D21).
- Sair de uma troca no meio castiga o console em alguns jogos (PLA, LGPE, BDSP, Z-A). O cancelar tem
  que ser educado (D21).
