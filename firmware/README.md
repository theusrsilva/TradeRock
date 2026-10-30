# Firmware ESP32

O TradeRock não compila firmware. Ele embute as imagens completas ("merged") que o pokeldn publica no
Release `v0.49.1` (firmware 1.9.1, o mesmo do commit travado do submódulo), baixadas por
`scripts/fetch-pokeldn-firmware.sh` para `assets/firmware/pokeldn/` e conferidas pelo `SHA256SUMS.txt` de lá.
O app grava sempre a versão que traz embutida (D23).

- Um firmware para todos os jogos, inclusive o FRLG.
- Uma imagem por chip, todas gravadas em `0x0`: ESP32 clássico (`pokeldn-radio.bin`, pela porta do conversor
  USB-serial), S3, C3 e C6 (`pokeldn-radio-s3.bin`, `-c3`, `-c6`, pela USB nativa). Flash mínima de 4 MB.
- O ESP32-S2 e o ESP32-H2 não servem. Detalhes das placas em `engines/pokeldn/docs/hardware_esp32.md`.

Onde está cada parte:

- **Manifesto:** `app/TradeRock.Core/Esp32/Esp32Firmware.cs` (chip, arquivo, flash, interface). As imagens
  entram no app como recursos embutidos e são conferidas pelo `SHA256SUMS.txt` a cada uso.
- **Gravação:** `worker/traderock_worker/esp32_flash.py`, com a API do esptool 5.4 (`detect_chip`,
  `write_flash`, `verify_flash`), rodando como o usuário no Python embutido. Conexão identifica o chip antes
  e só grava a imagem do chip certo, numa flash do tamanho certo.
- **Conversa com a placa:** `worker/traderock_worker/pokeldn_board.py` (HELLO e varredura do teste com o
  Switch) e `pokeldn_trade.py` (troca), os dois com o pokeldn embutido.
