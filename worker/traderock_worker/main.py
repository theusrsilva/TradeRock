"""Ponto de entrada: o servidor (padrão) ou um dos processos filhos (--child trade|card)."""

import json
import os


def run(argv, root):
    engine_dir = os.path.join(root, "engine")
    if argv[:1] == ["--child"] and len(argv) == 2:
        if argv[1] == "trade":
            from . import trade_child
            return trade_child.main(engine_dir)
        if argv[1] == "card":
            from . import card_child
            return card_child.main()
        return 2
    from . import server
    return server.main(install_info(root))


def install_info(root):
    """Versão do motor e patches aplicados (gravado por scripts/build-worker.sh)."""
    try:
        with open(os.path.join(root, "BUILD-INFO.json")) as f:
            info = json.load(f)
        return {k: info[k] for k in ("engineCommit", "patches", "python") if k in info}
    except (OSError, ValueError):
        return {}
