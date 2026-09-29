"""Dublês de `trio` e da biblioteca `ldn` para testar o beacon_child sem root, placa ou trio.

- `FakeTrio`: o pedaço do trio que o teste usa (run, sleep, open_nursery), sobre asyncio.
- `FakeLdn`: KeyDerivation, Scanner, load_keys e wlan (create_factory, create_monitor,
  is_valid_channel), com o mesmo comportamento observável da ldn 0.0.17: o Scanner devolve só
  anúncios que decifram e registra "Failed to parse advertisement frame" no logger `ldn` quando não.
"""

import asyncio
import contextlib
import logging
import types

ldn_logger = logging.getLogger("ldn")
FRLG_ID = 0x0100610011000000
OTHER_ID = 0x0100ABCD00000000


class _Nursery:
    def __init__(self):
        self.tasks = []
        self.cancel_scope = types.SimpleNamespace(cancel=self._cancel)

    def start_soon(self, fn, *args):
        self.tasks.append(asyncio.ensure_future(fn(*args)))

    def _cancel(self):
        for t in self.tasks:
            t.cancel()


@contextlib.asynccontextmanager
async def _open_nursery():
    nursery = _Nursery()
    try:
        yield nursery
    finally:
        await asyncio.gather(*nursery.tasks, return_exceptions=True)


FakeTrio = types.SimpleNamespace(run=lambda fn, *args: asyncio.run(fn(*args)), sleep=asyncio.sleep, open_nursery=_open_nursery)


class Network:
    def __init__(self, comm_id, channel, protocol=1, app_data=b""):
        self.address = f"mac-{comm_id:x}-{channel}"
        self.local_communication_id = comm_id
        self.channel = channel
        self.protocol = protocol
        self.num_participants = 1
        self.max_participants = 2
        self.application_data = app_data


class FakeMonitor:
    """`air`: canal → o que o Switch anuncia nele ("frlg", "other", "shaped" ou "bad")."""

    def __init__(self, air, bad_channels):
        self.air = air
        self.bad_channels = bad_channels
        self.channel = None
        self.visits = []
        self.pending = asyncio.Queue()

    async def set_channel(self, channel):
        if channel in self.bad_channels:
            raise OSError(22, "Invalid argument")
        self.channel = channel
        self.visits.append(channel)
        if channel in self.air:
            self.pending.put_nowait((channel, self.air[channel]))


class FakeScanner:
    def __init__(self, derivations, monitor):
        self.derivations = derivations
        self.monitor = monitor

    async def receive(self):
        while True:
            channel, what = await self.monitor.pending.get()
            if what == "bad":
                ldn_logger.warning("Failed to parse advertisement frame, ignoring it")
                continue
            if what == "frlg":
                return Network(FRLG_ID, channel)
            if what == "shaped":
                return Network(0x0100FFFF00000000, channel, app_data=b"FRLG-SHAPE...")
            return Network(OTHER_ID, channel, protocol=3)


def make_ldn(air=None, bad_channels=(), monitor_error=None):
    air = air or {}
    state = {}

    @contextlib.asynccontextmanager
    async def create_monitor(phy, ifname):
        if monitor_error:
            raise OSError(95, monitor_error)
        state["monitor"] = FakeMonitor(air, set(bad_channels))
        yield state["monitor"]

    @contextlib.asynccontextmanager
    async def create_factory():
        yield types.SimpleNamespace(create_monitor=create_monitor)

    wlan = types.SimpleNamespace(
        create_factory=create_factory,
        is_valid_channel=lambda c: c in (1, 6, 11, 36, 40, 44, 48),
    )
    ldn = types.SimpleNamespace(
        KeyDerivation=lambda keys, protocol: ("kd", protocol),
        Scanner=FakeScanner,
        load_keys=lambda path: {},
    )
    return ldn, wlan, state
