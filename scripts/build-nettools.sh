#!/usr/bin/env bash
# Compila `iw` e `ip` (iproute2) estáticos para o worker, sem depender de nada instalado no sistema:
# o compilador é o Zig (um tarball só, com musl e cabeçalhos do kernel embutidos) e as fontes vêm dos
# sites oficiais, com checksum fixo. O motor chama `iw` e `ip` pelo nome; o worker põe a pasta destes
# binários na frente do PATH dos processos filhos, então a pessoa não precisa instalar nada.
#
#   scripts/build-nettools.sh   → imprime a pasta com iw e ip (em cache; recompila só se mudar versão)
set -euo pipefail
cd "$(dirname "$0")/.."

ZIG_VERSION=0.16.0
ZIG_SHA256=70e49664a74374b48b51e6f3fdfbf437f6395d42509050588bd49abe52ba3d00
IW_VERSION=6.17
IW_SHA256=7d182e498289ab39b257da6780d562e415377107f50358ee5b55b8cfe40b1e33
IPROUTE2_VERSION=7.2.0
IPROUTE2_SHA256=4c2fa124c2cf0afd7ca34d1eeacba6ba048a56f6374e2aab93dafbdbd4eea9c0
LIBNL_VERSION=3.12.0
LIBNL_SHA256=fc51ca7196f1a3f5fdf6ffd3864b50f4f9c02333be28be4eeca057e103c0dd18

CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/traderock"
SRC="$CACHE/nettools-src"
OUT="$CACHE/nettools-iw$IW_VERSION-iproute2$IPROUTE2_VERSION-libnl$LIBNL_VERSION"
if [[ -x "$OUT/iw" && -x "$OUT/ip" ]]; then
  echo "$OUT"
  exit 0
fi

fetch() { # url sha256 arquivo
  local url=$1 sha=$2 file="$SRC/$3"
  if [[ ! -f "$file" ]]; then
    echo "==> baixando $3" >&2
    curl -fsSL -o "$file.part" "$url"
    mv "$file.part" "$file"
  fi
  echo "$sha  $file" | sha256sum -c --quiet
}

mkdir -p "$SRC"
fetch "https://ziglang.org/download/$ZIG_VERSION/zig-x86_64-linux-$ZIG_VERSION.tar.xz" "$ZIG_SHA256" "zig-x86_64-linux-$ZIG_VERSION.tar.xz"
fetch "https://mirrors.edge.kernel.org/pub/software/network/iw/iw-$IW_VERSION.tar.xz" "$IW_SHA256" "iw-$IW_VERSION.tar.xz"
fetch "https://mirrors.edge.kernel.org/pub/linux/utils/net/iproute2/iproute2-$IPROUTE2_VERSION.tar.xz" "$IPROUTE2_SHA256" "iproute2-$IPROUTE2_VERSION.tar.xz"
fetch "https://github.com/thom311/libnl/releases/download/libnl${LIBNL_VERSION//./_}/libnl-$LIBNL_VERSION.tar.gz" "$LIBNL_SHA256" "libnl-$LIBNL_VERSION.tar.gz"

WORK=$(mktemp -d "$CACHE/nettools-build.XXXXXX")
LOG="$WORK/build.log"
trap 'status=$?; if [[ $status -ne 0 && -f "$LOG" ]]; then tail -40 "$LOG" >&2; fi; rm -rf "$WORK"' EXIT
cd "$WORK"
for f in "zig-x86_64-linux-$ZIG_VERSION.tar.xz" "iw-$IW_VERSION.tar.xz" "iproute2-$IPROUTE2_VERSION.tar.xz" "libnl-$LIBNL_VERSION.tar.gz"; do
  tar xf "$SRC/$f"
done

ZIG="$WORK/zig-x86_64-linux-$ZIG_VERSION/zig"
export ZIG_GLOBAL_CACHE_DIR="$CACHE/zig-cache" ZIG_LOCAL_CACHE_DIR="$WORK/zig-local"
mkdir -p bin
# Wrappers: os Makefiles esperam comandos simples (CC, AR, RANLIB).
printf '#!/bin/sh\nexec "%s" cc -target x86_64-linux-musl "$@"\n' "$ZIG" > bin/cc
printf '#!/bin/sh\nexec "%s" ar "$@"\n' "$ZIG" > bin/ar
printf '#!/bin/sh\nexec "%s" ranlib "$@"\n' "$ZIG" > bin/ranlib
chmod +x bin/*
export PATH="$WORK/bin:$PATH" CC=cc AR=ar RANLIB=ranlib
JOBS=$(nproc 2>/dev/null || echo 2)
PREFIX="$WORK/prefix"

# pkg-config mínimo (o sistema pode não ter): o configure do libnl só confere que ele existe, e o
# Makefile do iw pergunta pela libnl, que é a recém-compilada. Qualquer outra biblioteca: "não há".
cat > bin/pkg-config <<EOF
#!/bin/sh
case "\$*" in
  --version) echo 0.29.2 ;;
  *--atleast-pkgconfig-version*) exit 0 ;;
  *--atleast-version*libnl-3.0*|*--exists*libnl*) exit 0 ;;
  *--cflags*libnl*) echo "-I$PREFIX/include/libnl3" ;;
  *--libs*libnl*) echo "-L$PREFIX/lib -lnl-genl-3 -lnl-3" ;;
  *) exit 1 ;;
esac
EOF
chmod +x bin/pkg-config

# Só libnl-3 e libnl-genl-3 (o iw não precisa de mais). bison/flex servem apenas à gramática da
# libnl-route, que não é compilada; o configure só confere se existem.
echo "==> libnl $LIBNL_VERSION (estática)" >&2
(cd "libnl-$LIBNL_VERSION" \
  && FLEX=flex-nao-usado YACC=bison-nao-usado \
     ./configure --quiet --host=x86_64-linux-musl --prefix="$PREFIX" \
       --disable-shared --enable-static --disable-cli --disable-debug >>"$LOG" 2>&1 \
  && make -s -j"$JOBS" lib/libnl-3.la lib/libnl-genl-3.la >>"$LOG" 2>&1 \
  && mkdir -p "$PREFIX/lib" "$PREFIX/include/libnl3" \
  && cp lib/.libs/libnl-3.a lib/.libs/libnl-genl-3.a "$PREFIX/lib/" \
  && cp -r include/netlink "$PREFIX/include/libnl3/")


echo "==> iw $IW_VERSION (estático)" >&2
make -s -C "iw-$IW_VERSION" -j"$JOBS" PKG_CONFIG=pkg-config LDFLAGS=-static V=0 >>"$LOG" 2>&1

echo "==> ip (iproute2 $IPROUTE2_VERSION, estático)" >&2
(cd "iproute2-$IPROUTE2_VERSION" \
  && PKG_CONFIG=false ./configure >>"$LOG" 2>&1 \
  && make -s -j"$JOBS" SUBDIRS="lib ip" LDFLAGS=-static >>"$LOG" 2>&1)

mkdir -p "$OUT.tmp"
cp "iw-$IW_VERSION/iw" "iproute2-$IPROUTE2_VERSION/ip/ip" "$OUT.tmp/"
for tool in iw ip; do
  # Sem símbolos de depuração (em outro arquivo: o objcopy não reescreve no lugar).
  if { command -v strip >/dev/null && strip --strip-all -o "$OUT.tmp/$tool.stripped" "$OUT.tmp/$tool" 2>/dev/null \
       || "$ZIG" objcopy --strip-all "$OUT.tmp/$tool" "$OUT.tmp/$tool.stripped" 2>/dev/null; } \
     && { "$OUT.tmp/$tool.stripped" -V >/dev/null 2>&1 || "$OUT.tmp/$tool.stripped" --version >/dev/null 2>&1; }; then
    mv "$OUT.tmp/$tool.stripped" "$OUT.tmp/$tool"
  fi
  rm -f "$OUT.tmp/$tool.stripped"
done
# Conferência: estáticos e rodando.
"$OUT.tmp/iw" --version >/dev/null
"$OUT.tmp/ip" -V >/dev/null
rm -rf "$OUT"
mv "$OUT.tmp" "$OUT"
echo "$OUT"
