# Patches aplicados ao frlg-ldn-trade

O submódulo `engines/frlg-ldn-trade` fica intocado. `scripts/build-worker.sh` copia o motor e aplica
estes patches (`patch -p0`) na cópia que vai para o worker.

Os patches 0001 e 0002 são do **[FRLG-LDN-Trade-Console](https://github.com/ImaEternal/FRLG-LDN-Trade-Console)**
(ImaEternal), commit `203bbe9`, pasta `patches/`, trazidos sem alteração. Como mudam um código AGPL-3.0,
eles também são AGPL-3.0, a mesma licença do TradeRock. Os detalhes e o motivo de cada mudança
estão no [README original](https://github.com/ImaEternal/FRLG-LDN-Trade-Console/blob/main/patches/README.md).
Em resumo:

| Patch | O que muda |
| --- | --- |
| `0001-transport-scan-and-retry-fixes.patch` | Varredura mais longa (0,6 s por canal) e nos canais de 5 GHz. Tempo de entrada que inclui a espera pela aprovação no Switch. Limpeza entre tentativas. Não entra na sala de outro Switch por perto. Monitor auxiliar que corrige a recepção no ALFA AWUS036ACHM (mt76x0u). Tolerância a `EALREADY` no registro de quadros. |
| `0002-frlgtrade-save-at-commit.patch` | Corrige `run_live`, que passava `args` em vez de `run_config` para `save_received`. Com isso, o Pokémon recebido é gravado no instante da troca, e não só no fim. |
| `0003-trade-save-chain-after-evolution.patch` | Do TradeRock. Quando o Pokémon evolui na troca, o Switch fica sem mandar as barreiras do salvamento durante a evolução (e espera a pessoa se o Pokémon aprende um golpe). O motor encerrava o salvamento depois de 10 s e o Switch travava em "Saving...". Agora o limite é de 60 s, e se o Switch voltar às barreiras depois disso o motor retoma o salvamento. |

Os ajustes do patch 0001 são lidos de variáveis de ambiente (`FRLG_*`). O worker define
`FRLG_JOIN_ATTEMPTS=1`, porque o retry fica no TradeRock, e `FRLG_JOIN_IP_WAIT`, o tempo de espera
pela aprovação.

Se o submódulo for atualizado e um patch deixar de aplicar, o build falha. Nesse caso, confira se o
upstream já incorporou a correção.
