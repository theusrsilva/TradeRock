# Firmware ESP32

O TradeRock não compila firmware. Ele embute os binários completos que o próprio easyworld
publica em `engines/frlg-ldn-trade-esp32/firmware/release/`, conferidos pelo `SHA256SUMS.txt`
de lá. O app grava sempre a versão que traz embutida.

- ESP-IDF fixado pelo upstream: **v6.1, commit `fff9895c82d744c7237be8847347bdd1b07c6643`**.
  O firmware usa interfaces privadas do ESP-IDF, então a versão não pode ser trocada.
- Um binário por chip: ESP32 clássico (4 MB, exige o módulo **ESP32-WROOM-32E**), C3 (4 MB, USB
  nativo), C6 (16 MB) e S3 (16 MB, UART0). Todos gravados em `0x0`, DIO, 80 MHz.

Onde está cada parte:

- **Manifesto:** `app/TradeRock.Core/Esp32/Esp32Firmware.cs` (chip, arquivo, flash, interface). Os
  binários entram no app como recursos embutidos e são conferidos pelo `SHA256SUMS.txt` a cada uso.
- **Gravação:** `worker/traderock_worker/esp32_flash.py`, com a API do esptool 5.4 (`detect_chip`,
  `write_flash`, `verify_flash`), rodando como o usuário no Python embutido. A etapa 4 do assistente
  identifica o chip antes e só grava o binário do chip certo, numa flash do tamanho certo.
