# Patches aplicados ao núcleo C# do easyworld (frlg-ldn-trade-esp32)

O submódulo `engines/frlg-ldn-trade-esp32` fica intocado. O projeto `app/Frlg.Trade.Core.Patched`
copia `host/core/*.cs` para `obj/` a cada build e aplica estes patches (`patch -p1`). São código do
TradeRock sobre código AGPL-3.0, portanto também AGPL-3.0.

## 0001-traderock-treinador-limite-chaves.patch

| Arquivo | O que muda | Por quê |
| --- | --- | --- |
| `Rfu.cs` | `TrainerProfile` (nome, ID de 32 bits, versão do jogo) usado no bloco do jogador, no cartão de treinador e nos dados do NI. O padrão reproduz os bytes originais (EMU / 0x47ED8822 / LeafGreen). | O treinador simulado era fixo; o TradeRock deixa escolher nome, TID/SID e FireRed/LeafGreen, como no motor da placa Wi-Fi. Os campos seguem `frlgsim/linkplayer.py` e `frlgsim/ni.py`. |
| `Rfu.cs` | `Rfu.Name` codifica a pontuação da tabela Gen III (espaço, `.`, `-`, `♂`, `♀`…), como `frlgsim/charmap.py`. | Antes, qualquer caractere fora de A–Z/a–z/0–9 virava 0. |
| `TradeEngine.cs` | Parâmetro `tradeLimit`: depois de N trocas, o motor cancela e sai da sala sozinho, pelo mesmo caminho de quando o Switch cancela (`leaving`). | O TradeRock faz de 1 a 6 trocas por sessão e sai sozinho, como o motor da placa Wi-Fi. |
| `Simulator.cs` | Os dados do NI usam o treinador do `TradeEngine`. | Consequência do primeiro item. |
| `TradeSession.cs` | `Run` recebe o `KeyFile`, o limite de trocas e o treinador. Aceita os dois comm ids do FRLG já vistos (`KnownIds`). | O prod.keys vem do app (permissão 600), não da pasta do executável nem de `~/.switch`. O id muda por versão do app: o frlg-ldn-trade usa `0x0100610011000000` e o easyworld `0x01006fa0233f8000`; o motor da placa Wi-Fi aceita os dois. |

Se o submódulo for atualizado e o patch deixar de aplicar, o build falha.
