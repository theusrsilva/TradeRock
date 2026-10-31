**Which ESP32 to buy?** The **ESP32-S3**, **ESP32-C3**, **ESP32-C6** and the **classic ESP32** all
work. See the table below. We don't recommend stores. The **ESP32-S2** and **ESP32-H2** don't work.

- **ESP32-S3, C3 and C6:** use the chip's **native USB**. On boards with two ports, use the one marked
  "USB" (not "UART" or "COM"): the firmware doesn't answer through the other.
- **Classic ESP32:** uses the board's **USB-serial converter** port (CH340, CP210x). Some boards with
  the old **ESP32-WROOM-32** module can't hear the Switch; the Switch test shows whether yours works.
- Boards with an **external antenna** (like the XIAO ones) only work with the antenna attached.
- Use a **USB data cable**: if the PC doesn't see the board, swap the cable first.
