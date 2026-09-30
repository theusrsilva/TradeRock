"""Marcos do frlg-ldn-trade → eventos do TradeRock.

O motor narra o progresso com `lg.info(...)` / `self.info(...)` (o modo não-verboso do upstream).
O wrapper troca esse logger por um que emite eventos; esta tabela diz qual evento cada frase vira.
As frases são copiadas do código do submódulo (e dos patches); um teste confere que cada uma
ainda existe lá, para uma mudança no upstream quebrar o teste e não a troca.
"""

# (início da frase, evento). Comparação por prefixo: algumas frases têm números no fim.
MILESTONES = (
    ("Scanning for the FRLG network...", "scanning"),
    ("Joining the host...", "join_requested"),
    ("Authenticated with the host. Approve the join request", "join_requested"),
    ("Joined.", "log"),                          # LDN associado; a conexão Pia ainda não foi confirmada
    ("Host acknowledged our join.", "log"),
    ("Connection established.", "joined"),       # o Switch confirmou (Pia): daqui em diante não há retry
    ("Host accepted the link.", "log"),
    ("Join handshake complete.", "log"),
    ("Host entered the trade room.", "in_room"),
    ("Host sat down.", "host_sat"),
    ("Setting sit flag.", "log"),
    ("Exchanged trainer cards.", "cards_exchanged"),
    ("Exchanged player info with the host.", "log"),
    ("Trade menu open; received the host's party.", "trade_menu_open"),
    ("Offered our Pokémon; waiting on the host.", "trade_offered"),
    ("Confirming trade...", "confirming"),
    ("Trade confirmed.", "log"),                 # o recebido chega logo depois, com os bytes
    ("Trade cancelled (mutual).", "trade_cancelled"),
    ("Trade cancelled by one side; back at the menu.", "trade_cancelled"),
    ("Declining the trade; cancelling to leave.", "leaving"),
    ("Cannot keep our last living Pokémon; cancelling to leave.", "leaving"),
    ("Host offered an illegitimate Pokémon; cancelling to leave.", "leaving"),
    ("Cannot trade slot", "leaving"),
    ("All ", "leaving"),                         # "All N trade(s) complete; cancelling to leave."
    ("Cancelling the trade menu...", "leaving"),
    ("Host is leaving the room...", "leaving"),
    ("Leaving the room.", "leaving"),
    ("Host rejected our join; leaving.", "error"),
    ("Closing the link...", "link_closed"),
    ("Link closed.", "link_closed"),
    ("Loaded ", "log"),                          # "Loaded N party Pokémon (offering slot K)."
    ("Received", "log"),
    ("  trade ", "log"),
)


def classify(message):
    for prefix, kind in MILESTONES:
        if message.startswith(prefix):
            return kind
    return "log"


# Falhas de entrada (RuntimeError de LiveTransport.start / ConnectionError do patch) → código.
# Ordem importa: a primeira que casar vence.
JOIN_FAILURES = (
    ("no joinable FRLG network", "no_network_found", True),
    ("none listed us", "join_not_approved", True),
    ("no advertisements arrived after associating", "radio_rx_failed", True),
    ("missing dep for live mode", "engine_broken", False),
    ("LDN join timed out", "join_failed", True),
)


def join_failure(text):
    for needle, code, retryable in JOIN_FAILURES:
        if needle in text:
            return code, retryable
    return "join_failed", True
