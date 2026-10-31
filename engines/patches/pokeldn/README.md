# Patches aplicados ao pokeldn

O submódulo `engines/pokeldn` fica intocado. `scripts/stage-pokeldn.sh` copia o pokeldn e aplica estes
patches (`patch -p0`) na cópia que vai para o worker e na cópia em que rodam os testes dele. Como mudam
um código AGPL-3.0, eles também são AGPL-3.0, a mesma licença do TradeRock.

Todos só agem com `TRADEROCK=1` no ambiente, que o worker define. Sem essa variável o pokeldn se
comporta como o upstream, e os testes dele continuam valendo.

| Patch | O que muda |
| --- | --- |
| `0001-traderock-sem-internet-plugins-e-pkhex.patch` | `app/update._open`, por onde passam as atualizações, os sprites, a loja de plugins e a galeria de eventos, recusa com `OSError` (D17). `plugins.load` não carrega plugin nenhum. `pokemon.prepare_offer`, `pokemon.validate` (a oferta do Legends: Arceus) e `pokemon.prepare` (o `swsh_connect.py`, quando o TradeRock entra na raid do console) não chamam os plugins nem o ajudante `pokeldn-pkhex`: o TradeRock já conferiu o Pokémon com o próprio PKHeX (só avisa, não bloqueia) e manda a forma de equipe. `PkhexService.validate_gift` (presente do Sword/Shield) confere só o tamanho e a soma do cartão, sem o ajudante (D26). |

Se o submódulo for atualizado e um patch deixar de aplicar, o build falha. Nesse caso, confira se o
upstream mudou o trecho.
