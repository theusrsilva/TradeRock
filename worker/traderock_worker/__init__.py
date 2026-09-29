"""Worker privilegiado do TradeRock (só Linux, só para o motor com a placa Wi-Fi).

Roda como root, iniciado pelo app via pkexec com a política polkit do TradeRock. Conversa com o
app por linhas JSON na entrada e na saída padrão (só o processo que o iniciou fala com ele). Quando
a entrada fecha (o app saiu ou travou), cancela o que estiver rodando, devolve o rádio ao estado
anterior e sai.
"""

VERSION = "0.6.0"
PROTOCOL = 1
