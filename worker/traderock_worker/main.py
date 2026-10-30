"""Ponto de entrada: o servidor (padrão), um dos processos filhos (--child trade|card|beacon), a gravação do ESP32
(--esp32) ou o pokeldn (--pokeldn board|trade). Os dois últimos rodam como o usuário: o app chama o Python embutido
direto, sem pkexec."""

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
        if argv[1] == "beacon":
            from . import beacon_child
            return beacon_child.main(engine_dir)
        if argv[1] == "pokeldn":  # como root, com a placa Wi-Fi (o worker escreve as chaves e escolhe a placa)
            from . import pokeldn_trade
            return pokeldn_trade.main(root)
        return 2
    if argv == ["--esp32"]:
        from . import esp32_flash
        return esp32_flash.main()
    # pokeldn (D23): placa ESP32 e troca com os jogos do Switch, como o usuário (sem pkexec) no ESP32.
    if argv == ["--pokeldn", "board"]:
        from . import pokeldn_board
        return pokeldn_board.main(root)
    if argv == ["--pokeldn", "trade"]:
        from . import pokeldn_trade
        return pokeldn_trade.main(root)
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
