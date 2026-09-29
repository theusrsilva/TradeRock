# Firmware ESP32

O TradeRock não compila firmware. Ele embute os binários completos que o próprio easyworld
publica em `engines/frlg-ldn-trade-esp32/firmware/release/`, conferidos pelo `SHA256SUMS.txt`
de lá. O app grava sempre a versão que traz embutida.

- ESP-IDF fixado pelo upstream: **v6.1, commit `fff9895c82d744c7237be8847347bdd1b07c6643`**.
  O firmware usa interfaces privadas do ESP-IDF, então a versão não pode ser trocada.
- Um binário por chip: ESP32 clássico (4 MB, exige o módulo **ESP32-WROOM-32E**), C3 (4 MB, USB
  nativo), C6 (16 MB) e S3 (16 MB, UART0). Todos gravados em `0x0`, DIO, 80 MHz.

O manifesto dos binários e a gravação pelo app chegam na Entrega 9.
