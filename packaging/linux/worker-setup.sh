#!/bin/sh
# Instala ou remove o componente de rede (worker) embutido no AppImage. Roda como root, sempre
# por "pkexec /bin/sh -c <este script>" depois de a pessoa confirmar no app (BundledWorker.cs): o texto
# vai na linha de comando, não como arquivo, e o worker chega como .tar pela entrada padrão.
#   $1 = install | uninstall   $2 = pasta do worker (…/traderock)   $3 = arquivo da política (….policy)
# Tudo fica com dono root e sem escrita para outros usuários; a troca da pasta é um "mv".
set -eu
umask 022
mode="$1"; prefix="$2"; policy="$3"
case "$prefix" in /*/traderock) ;; *) echo "pasta inválida: $prefix" >&2; exit 64 ;; esac
case "$policy" in /*.policy) ;; *) echo "política inválida: $policy" >&2; exit 64 ;; esac
if [ "$mode" = uninstall ]; then
  rm -rf "$prefix" "$policy"
  exit 0
fi
[ "$mode" = install ] || exit 64
parent=$(dirname "$prefix")
mkdir -p "$parent" "$(dirname "$policy")"
tmp=$(mktemp -d "$parent/.traderock-new.XXXXXX")
trap 'rm -rf "$tmp"' EXIT
tar -x -f - -C "$tmp" --no-same-owner
[ -x "$tmp/traderock-worker" ] || { echo "worker incompleto" >&2; exit 65; }
[ -f "$tmp/polkit.policy" ] || { echo "política ausente" >&2; exit 65; }
chmod -R u+rwX,go+rX,go-w "$tmp"
chmod 755 "$tmp"
mv "$tmp/polkit.policy" "$policy.new"
chmod 644 "$policy.new"
rm -rf "$prefix"
mv "$tmp" "$prefix"
trap - EXIT
mv "$policy.new" "$policy"
