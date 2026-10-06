On Windows, the radio is always an **ESP32** on USB. Windows doesn't give the low-level Wi-Fi access
the Switch's local wireless needs, so the PC's Wi-Fi card doesn't work, and you **don't need** any
special network card.

**USB driver:** boards with a **CH340** or **CP210x** converter may need a driver. Windows usually
installs it by itself through Windows Update; if it doesn't, the ESP32: firmware and USB port item warns you and shows the link to
the manufacturer's official page. Boards connected through **native USB** (ESP32-C3, or the S3 on
its "USB" port) use the driver that already comes with Windows.
