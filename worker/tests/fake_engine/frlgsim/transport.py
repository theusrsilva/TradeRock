"""Imitação mínima de frlgsim/transport.py com os patches (nomes usados pelo beacon_child)."""


class LiveTransport:
    LOCAL_COMMUNICATION_ID = 0x0100610011000000
    KNOWN_COMM_IDS = (0x0100610011000000, 0x01006FA0233F8000)


def _looks_like_frlg_beacon(app_data):
    return bytes(app_data or b"").startswith(b"FRLG-SHAPE")
