"""Trava de rede para os processos que rodam o pokeldn (D17, D21).

O TradeRock nunca acessa a internet. O pokeldn tem partes online (atualizações, sprites, plugins, troca
online); o patch 0001 desliga essas partes, e esta trava é a segunda camada: um audit hook do Python
(PEP 578) que recusa, no processo inteiro, qualquer conexão ou envio IP para fora da rede local e
qualquer resolução de nome. Netlink, AF_PACKET e serial não são IP e passam direto.

Um audit hook não pode ser removido: `install()` vale até o fim do processo.
"""

import ipaddress
import socket
import sys

_IP_FAMILIES = (socket.AF_INET, socket.AF_INET6)
_installed = False


class BlockedNetwork(PermissionError):
    pass


def local_address(host):
    """Endereço da rede local: privado, link-local, loopback, multicast ou broadcast."""
    if host in ("", "<broadcast>"):
        return True
    try:
        ip = ipaddress.ip_address(str(host).split("%", 1)[0])
    except ValueError:
        return False  # nome de host: resolver já seria consultar a rede
    return ip.is_private or ip.is_link_local or ip.is_loopback or ip.is_multicast


def _check_address(sock, address):
    if getattr(sock, "family", None) not in _IP_FAMILIES:
        return
    if isinstance(address, tuple) and address and not local_address(address[0]):
        raise BlockedNetwork(f"TradeRock: conexão fora da rede local recusada ({address[0]})")


def _hook(event, args):
    if event in ("socket.connect", "socket.sendto"):
        _check_address(args[0], args[1])
    elif event == "socket.sendmsg":
        if len(args) > 1 and args[1] is not None:
            _check_address(args[0], args[1])
    elif event == "socket.getaddrinfo":
        if args[0] is not None and not local_address(args[0].decode() if isinstance(args[0], bytes) else args[0]):
            raise BlockedNetwork(f"TradeRock: resolução de nome recusada ({args[0]!r})")
    elif event in ("socket.gethostbyname", "socket.gethostbyname_ex", "socket.gethostbyaddr"):
        if not local_address(args[0]):
            raise BlockedNetwork(f"TradeRock: resolução de nome recusada ({args[0]!r})")


def install():
    global _installed
    if not _installed:
        sys.addaudithook(_hook)
        _installed = True
