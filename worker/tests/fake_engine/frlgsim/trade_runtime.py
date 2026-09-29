"""Imitação de frlgsim/trade_runtime.py: ConsoleLog e received_paths com as mesmas assinaturas."""
import os
import time


class ConsoleLog:
    def __init__(self, verbose, prefix="", *, start=None, output=print):
        self.verbose = bool(verbose)
        self.prefix = prefix
        self.start = time.monotonic() if start is None else start
        self.output = output

    def __call__(self, *parts):
        if self.verbose:
            self.output(*parts)

    def info(self, *parts):
        if not self.verbose:
            self.output(*parts)


class FakeMon:
    def __init__(self, species, data):
        self.species = species
        self.species_name = f"SP{species}"
        self.data = data


def received_paths(mons, output_path, output_format, trades):
    if trades == 1:
        return [output_path] if mons else []
    stem, _ = os.path.splitext(output_path)
    return [f"{stem}_trade{i}_{m.species_name}.{output_format}" for i, m in enumerate(mons, 1)]


def save_received_mons(mons, *, output_path, output_size, output_format, trades, log=None):
    for mon, path in zip(mons, received_paths(mons, output_path, output_format, trades)):
        with open(path, "wb") as f:
            f.write(mon.data)
    return len(mons)
