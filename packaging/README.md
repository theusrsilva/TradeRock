# Empacotamento

| Caminho | Conteúdo |
| --- | --- |
| `linux/io.github.traderock.TradeRock.desktop` | Atalho do menu (.deb e AppImage) |
| `linux/io.github.traderock.TradeRock.svg` | Ícone (o mesmo do frontend) |
| `linux/worker-setup.sh` | Script que instala/remove o worker do AppImage como root (embutido no app, D17) |
| `../worker/polkit.policy.in`, `../worker/traderock-worker.in` | Política do polkit e launcher do worker (modelos) |
| `windows/` | Instalador (Inno Setup ou MSIX), Entrega 11 |

Os pacotes são montados por `scripts/package-linux.sh`. O app é publicado *self-contained* (não exige
o .NET instalado).

## .deb

| Caminho instalado | O quê |
| --- | --- |
| `/usr/lib/traderock/app/` | App (`traderock`) com o frontend embutido |
| `/usr/lib/traderock/{python,engine,worker,bin}` | Worker: Python embutido, motor com patches, `iw` e `ip` |
| `/usr/lib/traderock/traderock-worker` | Launcher que o polkit autoriza (`io.github.traderock.worker`) |
| `/usr/share/polkit-1/actions/io.github.traderock.worker.policy` | Política do polkit |
| `/usr/bin/traderock` | Link para o app |
| `/usr/share/applications`, `/usr/share/icons/hicolor/scalable/apps` | Atalho e ícone |
| `/usr/share/doc/traderock/` | `copyright`, `LICENSE`, `THIRD-PARTY-NOTICES.md` |

Tudo é do root e sem escrita para outros usuários (`dpkg-deb --root-owner-group`); o CI confere.
Dependências: `libwebkit2gtk-4.1-0`, `libgtk-3-0t64 | libgtk-3-0`, `libnotify4`, ICU (70 a 76),
`libssl3t64 | libssl3`, `pkexec | policykit-1`, `procps`.

## AppImage

- Runtime `type2-runtime` estático (não precisa da libfuse2), com sha256 fixo em `package-linux.sh`.
- `usr/lib/traderock/app/`: o mesmo app do .deb.
- `usr/lib/traderock/worker-payload/`: a árvore exata que vai para `/usr/local/lib/traderock`, com o
  launcher e a política já apontando para lá (`io.github.traderock.worker.appimage`), e o
  `PAYLOAD-ID` (versão + hash do conteúdo) que diz se a cópia instalada está atualizada.
- O WebKitGTK vem do sistema.
