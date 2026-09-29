"""Executado pelo launcher (traderock-worker) com o Python embutido em modo isolado (-I).

Com -I o Python não lê variáveis PYTHON*, nem o site do usuário, nem põe a pasta atual no
sys.path; por isso a pasta deste arquivo (do root, somente leitura) é incluída aqui.
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from traderock_worker.main import run  # noqa: E402

sys.exit(run(sys.argv[1:], os.path.dirname(HERE)))
