## Qual arquivo baixar

| Seu computador | Arquivo | Como instalar |
| --- | --- | --- |
| **Windows 10 ou 11** (64 bits) | `TradeRock-Setup-{{VERSION}}.exe` | Abra e siga os passos. Veja o aviso abaixo. |
| **Ubuntu, Debian** e derivados | `traderock_{{DEB_VERSION}}_amd64.deb` | `sudo apt install ./traderock_{{DEB_VERSION}}_amd64.deb` |
| **Outras distribuições Linux** | `TradeRock-{{VERSION}}-x86_64.AppImage` | `chmod +x` no arquivo e abra |

- **Windows:** o instalador ainda não tem assinatura digital, então o Windows mostra "O Windows
  protegeu o computador". Clique em **Mais informações → Executar assim mesmo**. No Windows, o rádio é
  sempre um **ESP32** ligado no USB.
- **Linux:** o rádio pode ser a placa Wi-Fi do próprio PC ou um ESP32.
- **Para atualizar,** use a tela **Sobre** do app (ele também avisa no Início quando há versão nova), ou baixe a versão nova e instale por cima. Seus Pokémon, contas e chaves continuam.
- **Conferir o download (opcional):** compare com o `SHA256SUMS`. No Linux, rode
  `sha256sum -c SHA256SUMS --ignore-missing`. No Windows, rode
  `Get-FileHash TradeRock-Setup-{{VERSION}}.exe` no PowerShell.

Nunca distribuímos prod.keys nem ROMs: use só as chaves extraídas do seu próprio Switch. O
código-fonte completo (AGPL-3.0) está nos arquivos `.tar.gz` deste Release.
