"""Imitação de frlgtrade.py: run_live narra um roteiro (FAKE_SCENARIO) pelo logger recebido."""
import os
import time

from frlgsim import trade_runtime as runtime


class Engine:
    def __init__(self):
        self.received_mons = []
        self.received_mon = None


def save_received(engine, run_config, lg):
    plan = run_config.plan
    return runtime.save_received_mons(engine.received_mons, output_path=plan.output_path,
                                      output_size=plan.output_size, output_format=plan.output_format,
                                      trades=plan.trades, log=lg)


def run_live(run_config, lg):
    scenario = os.environ.get("FAKE_SCENARIO", "success")
    engine = Engine()
    elog = runtime.ConsoleLog(lg.verbose, "  [trade]", start=lg.start)
    lg.info("Loaded 2 party Pokémon (offering slot 2).")
    lg.info("Scanning for the FRLG network...")
    if scenario == "no_network":
        raise RuntimeError("LDN join failed after 1 attempt(s):\nno joinable FRLG network (saw 0, 0 joinable)")
    if scenario == "bad_keys":
        import logging
        logging.getLogger("ldn").warning("Failed to parse advertisement frame, ignoring it")
        raise RuntimeError("LDN join failed after 1 attempt(s):\nno joinable FRLG network (saw 0, 0 joinable)")
    lg.info("Joining the host...")
    lg("[live] verbose line")
    if scenario == "hang":
        while True:
            time.sleep(0.05)
    lg.info("Joined.")
    lg.info("Connection established.")
    elog.info("Host entered the trade room.")
    elog.info("Host sat down.")
    elog.info("Exchanged trainer cards.")
    lg.info("Trade menu open; received the host's party.")
    for i in range(run_config.plan.trades):
        elog.info("Offered our Pokémon; waiting on the host.")
        if scenario == "host_cancels":
            elog.info("Trade cancelled by one side; back at the menu.")
            lg.info("Link closed.")
            return engine
        elog.info("Confirming trade...")
        elog.info("Trade confirmed.")
        engine.received_mons.append(runtime.FakeMon(25 + i, bytes([i]) * 100))
        save_received(engine, run_config, lg)  # como o patch 0002: grava no commit
    elog.info(f"All {run_config.plan.trades} trade(s) complete; cancelling to leave.")
    lg.info("Closing the link...")
    lg.info("Link closed.")
    return engine
