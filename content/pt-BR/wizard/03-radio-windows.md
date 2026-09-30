No Windows, o rádio é sempre um **ESP32** ligado no USB. O Windows não dá o acesso de baixo nível
ao Wi-Fi que o wireless local do Switch exige, então a placa Wi-Fi do PC não serve, e você **não
precisa** de nenhuma placa de rede especial.

**Driver do USB:** placas com conversor **CH340** ou **CP210x** podem precisar de um driver. O
Windows costuma instalar sozinho pelo Windows Update; se não instalar, a etapa 4 avisa e mostra o
link da página oficial do fabricante. Placas ligadas pela **USB nativa** (ESP32-C3, ou o S3 pela
porta "USB") usam o driver que já vem no Windows.
