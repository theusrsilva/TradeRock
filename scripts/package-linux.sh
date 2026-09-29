#!/usr/bin/env bash
# Monta os pacotes Linux do TradeRock em dist/:
#   traderock_<versão>_amd64.deb         app + worker em /usr/lib/traderock (worker de dono root)
#   TradeRock-<versão>-x86_64.AppImage   app + worker embutido, instalado pelo app na primeira vez
#
#   scripts/package-linux.sh [versão] [--sources]
#
# A versão padrão é a do Directory.Build.props; no CI ela vem da tag (v0.8.0 → 0.8.0).
# --sources também gera o código-fonte completo (com os submódulos, exigido pela AGPL) e o das
# ferramentas de rede embutidas (iproute2 GPL-2.0, iw ISC, libnl LGPL-2.1).
#
# Precisa de: .NET 10 SDK, Node, curl, dpkg-deb, mksquashfs (squashfs-tools). Nenhuma chave, ROM ou
# dado do usuário entra nos pacotes.
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=""
SOURCES=0
for arg in "$@"; do
  case "$arg" in
    --sources) SOURCES=1 ;;
    -*) echo "opção desconhecida: $arg" >&2; exit 2 ;;
    *) VERSION=${arg#v} ;;
  esac
done
VERSION=${VERSION:-$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' Directory.Build.props)}
if [[ ! $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
  echo "versão inválida: $VERSION" >&2; exit 2
fi
DEB_VERSION=${VERSION/-/\~} # 1.0.0-rc.1 → 1.0.0~rc.1 (vem antes da 1.0.0 no dpkg)

# Runtime do AppImage (AppImage/type2-runtime, MIT): estático, não precisa da libfuse2 do sistema.
# O upstream só publica o "continuous"; o arquivo fica no cache pelo sha256, e uma mudança lá fora
# falha aqui com uma mensagem clara (atualize o sha256 depois de conferir a origem).
RUNTIME_URL=https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64
RUNTIME_SHA256=${TRADEROCK_APPIMAGE_RUNTIME_SHA256:-156f4bdbde9c52d01814600013e0a273f0118dc2de98975f3c8c63427ec79074}
MAINTAINER=${TRADEROCK_MAINTAINER:-"TradeRock <https://github.com/theusrsilva/TradeRock/issues>"}
CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/traderock"
BUILD=build/package
DIST=dist
APP_ID=io.github.traderock.TradeRock

rm -rf "$BUILD"
mkdir -p "$BUILD" "$DIST" "$CACHE"

echo "==> app $VERSION (self-contained, linux-x64)"
dotnet publish app/TradeRock.App -c Release -r linux-x64 --self-contained \
  -p:BuildFrontend=true -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false \
  -o "$BUILD/app" --nologo -v quiet
[[ "$("$BUILD/app/traderock" --version)" == "TradeRock $VERSION" ]] || { echo "versão do app não confere" >&2; exit 1; }

echo "==> worker"
if [[ "${TRADEROCK_SKIP_WORKER_BUILD:-0}" != 1 || ! -f build/worker/stage/BUILD-INFO.json ]]; then
  scripts/build-worker.sh
fi
STAGE=build/worker/stage

# Launcher e política do polkit para um prefixo e uma ação (os mesmos modelos do install-worker.sh).
render() { # modelo prefixo ação
  sed -e "s|@PREFIX@|$2|g" -e "s|@ACTION_ID@|$3|g" "$1"
}

# Arquivos e pastas: dono root (dpkg-deb --root-owner-group / mksquashfs -root-owned), sem escrita para
# outros usuários; executáveis continuam executáveis.
normalize() {
  chmod -R u+rwX,go+rX,go-w "$1"
}

# ---------------------------------------------------------------------------------------------------
echo "==> .deb"
DEB="$BUILD/deb"
LIB="$DEB/usr/lib/traderock"
mkdir -p "$LIB" "$DEB/usr/bin" "$DEB/usr/share/applications" "$DEB/usr/share/icons/hicolor/scalable/apps" \
         "$DEB/usr/share/polkit-1/actions" "$DEB/usr/share/doc/traderock" "$DEB/DEBIAN"
cp -a "$BUILD/app" "$LIB/app"
cp -a "$STAGE/." "$LIB/"
render worker/traderock-worker.in /usr/lib/traderock io.github.traderock.worker > "$LIB/traderock-worker"
chmod 755 "$LIB/traderock-worker"
render worker/polkit.policy.in /usr/lib/traderock io.github.traderock.worker \
  > "$DEB/usr/share/polkit-1/actions/io.github.traderock.worker.policy"
ln -s ../lib/traderock/app/traderock "$DEB/usr/bin/traderock"
cp packaging/linux/$APP_ID.desktop "$DEB/usr/share/applications/"
cp packaging/linux/$APP_ID.svg "$DEB/usr/share/icons/hicolor/scalable/apps/"
cp THIRD-PARTY-NOTICES.md "$DEB/usr/share/doc/traderock/"
cat > "$DEB/usr/share/doc/traderock/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: TradeRock
Source: https://github.com/theusrsilva/TradeRock

Files: *
Copyright: TradeRock contributors
License: AGPL-3.0-or-later
 The full text is in /usr/share/doc/traderock/LICENSE. Third-party components and their licenses are
 listed in /usr/share/doc/traderock/THIRD-PARTY-NOTICES.md. The complete corresponding source code of
 every release, including the bundled engines and network tools, is published with the release.
EOF
cp LICENSE "$DEB/usr/share/doc/traderock/LICENSE"
normalize "$DEB"

# ICU e OpenSSL: o .NET carrega a versão que existir (22.04: icu70; Debian 12: icu72; 24.04: icu74;
# Debian 13: icu76). libgtk/libssl ganharam o sufixo t64 no Ubuntu 24.04 e no Debian 13.
cat > "$DEB/DEBIAN/control" <<EOF
Package: traderock
Version: $DEB_VERSION
Architecture: amd64
Maintainer: $MAINTAINER
Installed-Size: $(du -sk --exclude=DEBIAN "$DEB" | cut -f1)
Depends: libc6 (>= 2.31), libstdc++6, libgtk-3-0t64 | libgtk-3-0, libwebkit2gtk-4.1-0, libnotify4,
 libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3, pkexec | policykit-1, procps
Section: games
Priority: optional
Homepage: https://github.com/theusrsilva/TradeRock
Description: Trade Pokémon with FireRed and LeafGreen on Nintendo Switch
 TradeRock trades Pokémon with the Switch release of Pokémon FireRed and
 LeafGreen over the game's local wireless (LDN). The PC's Wi-Fi card (or an
 ESP32 over USB) acts as a second Switch in the Direct Corner trade room.
 .
 It also builds legal Pokémon (PKHeX + AutoMod) and keeps them in a vault.
 The Wi-Fi component runs as root through pkexec and polkit; the app itself
 never does. Uses only the prod.keys dumped from your own console.
EOF
(cd "$DEB" && find usr -type f -print0 | sort -z | xargs -0 md5sum) > "$DEB/DEBIAN/md5sums"
chmod 644 "$DEB/DEBIAN/control" "$DEB/DEBIAN/md5sums"
DEB_FILE="$DIST/traderock_${DEB_VERSION}_amd64.deb"
dpkg-deb --root-owner-group -Zxz --build "$DEB" "$DEB_FILE" >/dev/null
echo "    $DEB_FILE ($(du -h "$DEB_FILE" | cut -f1))"

# ---------------------------------------------------------------------------------------------------
echo "==> AppImage"
RUNTIME="$CACHE/appimage-runtime-$RUNTIME_SHA256"
if [[ ! -f "$RUNTIME" ]]; then
  curl -fsSL -o "$RUNTIME.part" "$RUNTIME_URL"
  if ! echo "$RUNTIME_SHA256  $RUNTIME.part" | sha256sum -c --quiet; then
    echo "O runtime do AppImage mudou no upstream ($RUNTIME_URL)." >&2
    echo "Confira a origem e atualize RUNTIME_SHA256 (ou TRADEROCK_APPIMAGE_RUNTIME_SHA256)." >&2
    rm -f "$RUNTIME.part"; exit 1
  fi
  mv "$RUNTIME.part" "$RUNTIME"
fi

APPDIR="$BUILD/AppDir"
ALIB="$APPDIR/usr/lib/traderock"
mkdir -p "$ALIB" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/scalable/apps" "$APPDIR/usr/share/doc/traderock"
cp -a "$BUILD/app" "$ALIB/app"

# Worker embutido: a árvore exata que vai para /usr/local/lib/traderock, mais a política do polkit.
# O app a envia como .tar para "pkexec /bin/sh -c <script>" (BundledWorker.cs).
PAYLOAD="$ALIB/worker-payload"
mkdir -p "$PAYLOAD"
cp -a "$STAGE/." "$PAYLOAD/"
render worker/traderock-worker.in /usr/local/lib/traderock io.github.traderock.worker.appimage > "$PAYLOAD/traderock-worker"
chmod 755 "$PAYLOAD/traderock-worker"
render worker/polkit.policy.in /usr/local/lib/traderock io.github.traderock.worker.appimage > "$PAYLOAD/polkit.policy"
normalize "$PAYLOAD"
# Identifica o conteúdo: muda quando qualquer arquivo do worker muda (o app oferece atualizar).
(cd "$PAYLOAD" && find . \( -type f -o -type l \) -print0 | LC_ALL=C sort -z \
  | xargs -0 sh -c 'for f; do if [ -L "$f" ]; then printf "%s -> %s\n" "$f" "$(readlink "$f")"; else sha256sum "$f"; fi; done' sh \
  | sha256sum | cut -d' ' -f1) > "$BUILD/payload-id"
echo "$VERSION $(cat "$BUILD/payload-id")" > "$PAYLOAD/PAYLOAD-ID"

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
# Ponto de entrada do AppImage.
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/traderock/app/traderock" "$@"
EOF
chmod 755 "$APPDIR/AppRun"
cp packaging/linux/$APP_ID.desktop "$APPDIR/$APP_ID.desktop"
cp packaging/linux/$APP_ID.desktop "$APPDIR/usr/share/applications/"
cp packaging/linux/$APP_ID.svg "$APPDIR/$APP_ID.svg"
cp packaging/linux/$APP_ID.svg "$APPDIR/usr/share/icons/hicolor/scalable/apps/"
ln -s $APP_ID.svg "$APPDIR/.DirIcon"
cp LICENSE THIRD-PARTY-NOTICES.md "$APPDIR/usr/share/doc/traderock/"
normalize "$APPDIR"

APPIMAGE="$DIST/TradeRock-$VERSION-x86_64.AppImage"
mksquashfs "$APPDIR" "$BUILD/AppDir.squashfs" -root-owned -noappend -comp zstd -quiet -no-progress >/dev/null
cat "$RUNTIME" "$BUILD/AppDir.squashfs" > "$APPIMAGE"
chmod 755 "$APPIMAGE"
echo "    $APPIMAGE ($(du -h "$APPIMAGE" | cut -f1))"

# ---------------------------------------------------------------------------------------------------
if [[ $SOURCES == 1 ]]; then
  echo "==> código-fonte"
  SRC_NAME="traderock-$VERSION-source"
  git ls-files -z --recurse-submodules \
    | tar --null -T - --transform "s|^|$SRC_NAME/|" --owner=0 --group=0 --numeric-owner -czf "$DIST/$SRC_NAME.tar.gz"
  # As fontes oficiais usadas por scripts/build-nettools.sh (sha256 conferido lá).
  NETSRC="$CACHE/nettools-src"
  tar -C "$NETSRC" --owner=0 --group=0 --numeric-owner -czf "$DIST/traderock-$VERSION-nettools-src.tar.gz" \
    $(cd "$NETSRC" && ls iw-*.tar.xz iproute2-*.tar.xz libnl-*.tar.gz) \
    -C "$PWD/scripts" build-nettools.sh
fi

(cd "$DIST" && sha256sum -- *.deb *.AppImage $( [[ $SOURCES == 1 ]] && echo '*.tar.gz') > SHA256SUMS)
echo "==> pronto:"
ls -la "$DIST"
