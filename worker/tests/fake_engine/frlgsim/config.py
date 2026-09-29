"""Imitação mínima de frlgsim/config.py (mesmos nomes e assinaturas usados pelo wrapper)."""
from dataclasses import dataclass


@dataclass(frozen=True)
class TrainerProfile:
    name: str
    tid: int
    sid: int
    version: str

    def __post_init__(self):
        if len(self.name) > 7 or "@" in self.name:
            raise ValueError("trainer name contains unsupported Gen III characters")


def profile_from_overrides(*, ot=None, version=None, trainer_id=None):
    tid, sid = trainer_id
    return TrainerProfile(ot, tid, 0x47ED if sid is None else sid, version)


@dataclass(frozen=True)
class TradePlan:
    party_paths: tuple
    output_path: str = "received.pk3"
    output_size: int = 100
    output_format: str = "pk3"
    trade_slot: int = 1
    offered_slots: tuple | None = None
    trades: int = 1


@dataclass(frozen=True)
class LdnConfig:
    password: bytes | None = None
    phy: str = "phy0"
    keys_path: str = ""


@dataclass(frozen=True)
class JoinerOptions:
    live: bool = True


@dataclass(frozen=True)
class TradeRunConfig:
    profile: object
    plan: object
    ldn: object
    role: object
