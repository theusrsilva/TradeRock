O wireless local do jogo é **criptografado**. O arquivo **prod.keys** tem as chaves do Switch que
permitem entender essa conversa. Sem ele, o TradeRock não consegue falar com o seu console.

**Como se obtém:** extraindo do **seu próprio** Switch com o **Lockpick_RCM**, num Switch 1 com
desbloqueio (modelos vulneráveis ou com modchip). O **Switch 2 não permite** extrair as chaves.

> ⚠ **Importante:** as chaves são material protegido da Nintendo. Use **só** as extraídas do seu
> próprio console. **Não baixe** da internet e **não compartilhe** o arquivo: parte das chaves é
> única do seu aparelho. O TradeRock nunca baixa chaves nem indica onde conseguir.

**O que o TradeRock faz com o arquivo:** guarda numa pasta só sua (permissão 600), usa apenas para
conversar com o Switch e **nunca** mostra os valores, grava em log, envia pela rede ou coloca em
backups, exportações ou relatórios.

<details><summary>Saiba mais</summary>

Aqui conferimos só o **formato**: se o arquivo é legível e tem as chaves que a biblioteca LDN usa
(`aes_kek_generation_source`, `aes_key_generation_source` e `master_key_00` ou `master_key_12`).
Isso **não prova** que os valores estão certos. A prova vem no **teste com o Switch** (etapa 6): se
o TradeRock conseguir decifrar a sala do seu jogo, as chaves estão certas.

</details>

Ainda não tem o arquivo? Dá para **pular** e explorar o app: criar Pokémon, organizar o cofre e
testar a troca em **modo demonstração**. Só a troca de verdade fica bloqueada.
