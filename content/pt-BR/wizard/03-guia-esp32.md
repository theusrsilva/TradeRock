**Qual ESP32 comprar?** Servem o **ESP32-S3**, o **ESP32-C3**, o **ESP32-C6** e o **ESP32 clássico**.
Veja a tabela abaixo. Não indicamos lojas. O **ESP32-S2** e o **ESP32-H2** não servem.

- **ESP32-S3, C3 e C6:** usam a **USB nativa** do chip. Em placas com duas portas, use a marcada
  "USB" (não a "UART" ou "COM"): pela outra o firmware não responde.
- **ESP32 clássico:** usa a porta do **conversor USB-serial** da placa (CH340, CP210x). Algumas
  placas com o módulo antigo **ESP32-WROOM-32** não ouvem o Switch; o teste com o Switch mostra se a
  sua serve.
- Placas com **antena externa** (como as XIAO) só funcionam com a antena ligada.
- Use um **cabo USB de dados**: se o PC não reconhece a placa, troque o cabo antes de tudo.
