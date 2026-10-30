"""Plugin do pytest para os testes do pokeldn no TradeRock: testes que falham só porque falta o flet
(a interface gráfica do pokeldn, que o TradeRock não usa nem instala) viram "skipped"."""
import pytest

MISSING = "No module named 'flet'"


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    report = outcome.get_result()
    if report.failed and MISSING in str(report.longrepr):
        report.outcome = "skipped"
        report.longrepr = (str(item.path), item.location[1] or 0, "Skipped: sem flet (interface gráfica do pokeldn)")
