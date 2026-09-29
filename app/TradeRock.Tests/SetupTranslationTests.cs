using System.Text.Json;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Setup;

namespace TradeRock.Tests;

/// <summary>Todo código de verificação e situação que o backend gera tem texto nos dois idiomas.</summary>
public sealed class SetupTranslationTests
{
    private static JsonElement Locale(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "TradeRock.slnx")))
            dir = Path.GetDirectoryName(dir)!;
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "frontend", "src", "locales", $"{lang}.json"))).RootElement.GetProperty("wizard");
    }

    private static IEnumerable<SetupCheck> AllChecks()
    {
        var catalog = HardwareCatalog.Embedded;
        var keysOptions = new KeysValidation?[] { null, new(true, [], [], [], [1], 3), new(false, [2], [], ["master_key_00"], [], 1) };
        foreach (var (_, system) in SetupScenarios.All)
        {
            var advice = RadioAdvisor.Advise(system, catalog);
            var phys = system.WifiCards.Select(c => (string?)c.Phy).Append(null);
            var ports = system.SerialPorts.Select(p => (string?)p.Path).Append(null);
            var boards = catalog.Esp32.Select(b => (string?)b.Id).Append(null);
            foreach (var keys in keysOptions)
            foreach (var radio in new[] { null, "wifi", "esp32" })
            foreach (var phy in phys)
            foreach (var port in ports)
            foreach (var board in boards)
            foreach (var confirmed in new[] { false, true })
            {
                var state = new SetupState { Radio = radio, Phy = phy, SerialPort = port, Esp32Board = board, Esp32ModuleConfirmed = confirmed, KeysSkipped = keys is null && confirmed };
                foreach (var check in SetupChecks.Evaluate(system, advice, state, keys, catalog))
                    yield return check;
            }
        }

        // Placa Wi-Fi: variações do worker, do teste da placa, das ferramentas e da rede.
        var linux = SetupScenarios.All["linux-compatible"];
        var workers = new WorkerInstall?[]
        {
            null,
            new("dev", "/x", true, true, true),
            new("dev", "/x", true, Safe: false, true),
            new("dev", "/x", PolicyInstalled: false, true, true),
            new("dev", "/x", true, true, PkexecFound: false),
        };
        var tests = new CardTestResult?[]
        {
            null,
            new("phy1", true, "passed", default, []),
            new("phy1", true, "passed_no_rx", default, []),
            new("phy1", true, "passed_some_channels", default, []),
            new("phy1", false, "tx_failed", default, []),
        };
        // Teste com o Switch: todos os resultados, com e sem a master_key_12.
        var saved = DateTimeOffset.UnixEpoch;
        foreach (var protocols in new[] { new[] { 1 }, new[] { 1, 3 } })
        foreach (var code in SwitchTestCodes.Append("found_decrypted"))
        {
            var keys = new KeysValidation(true, [], [], [], protocols, 3, saved);
            var sw = new SwitchTestResult("phy1", code == "found_decrypted", code, default, [], [], KeysSavedAt: saved);
            var state = new SetupState { Radio = "wifi", Phy = "phy1", SwitchTest = sw };
            foreach (var check in SetupChecks.Evaluate(linux, RadioAdvisor.Advise(linux, catalog), state, keys, catalog))
                yield return check;
        }

        // AppImage: worker embutido não instalado ou de outra versão.
        foreach (var bundled in new BundledWorkerStatus[] { new(true, false, false), new(true, true, false) })
        {
            var system = linux with { BundledWorker = bundled, Worker = bundled.Installed ? new("appimage", "/x", true, true, true) : null };
            foreach (var check in SetupChecks.Evaluate(system, RadioAdvisor.Advise(system, catalog), new SetupState { Radio = "wifi", Phy = "phy1" }, null, catalog))
                yield return check;
        }

        foreach (var worker in workers)
        foreach (var test in tests)
        foreach (var net in new[] { new LdnNetworkConfig(false, false), new LdnNetworkConfig(true, true) })
        {
            var system = linux with { Worker = worker, LdnNetwork = net, MissingTools = ["iw"] };
            var state = new SetupState { Radio = "wifi", Phy = "phy1", CardTest = test };
            foreach (var check in SetupChecks.Evaluate(system, RadioAdvisor.Advise(system, catalog), state, null, catalog))
                yield return check;
        }
    }

    /// <summary>
    /// Códigos que o worker e o motor da placa Wi-Fi podem devolver (worker/traderock_worker:
    /// events.py JOIN_FAILURES, trade_child.py, server.py; WorkerHost e PythonLdnEngine).
    /// </summary>
    public static readonly string[] WorkerFailureCodes =
    [
        "no_network_found", "beacon_not_decrypted", "join_not_approved", "radio_rx_failed", "engine_broken",
        "join_failed", "join_rejected", "host_cancelled", "connection_lost", "trade_incomplete", "engine_error",
        "invalid_request", "missing_tools", "phy_not_found", "busy", "not_root", "worker_missing", "worker_unsafe",
        "worker_policy_missing", "pkexec_missing", "auth_cancelled", "not_authorized", "worker_exited",
        "worker_timeout", "worker_version", "worker_start_failed", "wifi_not_configured", "keys_missing",
        "beacon_test_crashed", "worker_install_failed", "worker_not_bundled",
    ];

    /// <summary>Resultados do teste com o Switch (beacon_child.py verdict), menos o aprovado.</summary>
    public static readonly string[] SwitchTestCodes =
        ["found_not_decrypted", "other_session", "not_found", "monitor_failed", "channel_failed", "scan_failed", "engine_error", "cancelled"];

    /// <summary>Resultados do teste da placa (card_child.py verdict) e falhas do próprio teste.</summary>
    public static readonly string[] CardTestCodes =
        ["passed", "passed_no_rx", "passed_some_channels", "monitor_failed", "channel_failed", "tx_failed", "card_test_crashed"];

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en")]
    public void Falhas_do_worker_e_do_teste_da_placa_tem_texto(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "TradeRock.slnx")))
            dir = Path.GetDirectoryName(dir)!;
        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "frontend", "src", "locales", $"{lang}.json"))).RootElement;
        var failures = root.GetProperty("failures");
        var card = root.GetProperty("wizard").GetProperty("cardTest").GetProperty("codes");
        var sw = root.GetProperty("wizard").GetProperty("switchTest").GetProperty("codes");
        var hints = root.GetProperty("wizard").GetProperty("switchTest").GetProperty("hints");
        var reasons = root.GetProperty("settings").GetProperty("engineReasons");
        var missing = WorkerFailureCodes.Where(c => !failures.TryGetProperty(c, out _))
            .Concat(CardTestCodes.Where(c => !card.TryGetProperty(c, out _)))
            .Concat(SwitchTestCodes.Append("found_decrypted").Where(c => !sw.TryGetProperty(c, out _)).Select(c => "switchTest." + c))
            .Concat(new[] { "missing_master_key_12" }.Where(c => !hints.TryGetProperty(c, out _)))
            .Concat(new[] { "not_linux", "worker_missing", "wifi_not_configured" }.Where(c => !reasons.TryGetProperty(c, out _)))
            .ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en")]
    public void Codigos_de_verificacao_tem_texto(string lang)
    {
        var checks = Locale(lang).GetProperty("checks");
        var missing = AllChecks().Select(c => c.Code).Distinct().Where(code => !checks.TryGetProperty(code, out _)).ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en")]
    public void Situacoes_motivos_e_entregas_tem_texto(string lang)
    {
        var w = Locale(lang);
        var catalog = HardwareCatalog.Embedded;
        foreach (var system in SetupScenarios.All.Values)
        {
            var advice = RadioAdvisor.Advise(system, catalog);
            Assert.True(w.GetProperty("preview").TryGetProperty(advice.Situation, out _), advice.Situation);
            foreach (var reason in advice.Options.Select(o => o.ReasonCode).OfType<string>())
                Assert.True(w.GetProperty("reasons").TryGetProperty(reason, out _), reason);
        }
        foreach (var delivery in AllChecks().Select(c => c.AvailableIn).OfType<string>().Distinct())
            Assert.True(w.GetProperty("deliveries").TryGetProperty(delivery, out _), delivery);
    }
}
