using System.Text.Json.Nodes;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Storage;

namespace TradeRock.Core.Setup;

public interface ISystemProbe
{
    SystemSnapshot Read();
}

/// <summary>Leitura do sistema real. O Windows (só ESP32) ganha leitura completa na Fase 2.</summary>
public sealed class RealSystemProbe : ISystemProbe
{
    public SystemSnapshot Read()
    {
        if (OperatingSystem.IsLinux())
            return new LinuxProbe().Read();
        var os = OperatingSystem.IsWindows() ? "windows" : "other";
        return new SystemSnapshot(os, false, null, null, [], [], null, false, false);
    }
}

public sealed record SetupStatePatch(
    string? Radio = null,
    string? Phy = null,
    string? SerialPort = null,
    string? Esp32Board = null,
    bool? Esp32ModuleConfirmed = null,
    bool? KeysSkipped = null,
    bool? Completed = null);

/// <summary>Assistente de primeira execução: estado, leitura do sistema (ou simulação), chaves e verificações.</summary>
public sealed class SetupService(SettingsStore settings, ProdKeys keys, ISystemProbe probe, WorkerHost worker, BundledWorker bundled, TimeProvider time)
{
    public const string StateKey = "setup.state";
    public const string SimulationKey = "setup.simulation";

    /// <summary>Simulação: a configuração de rede "instalada" (não há worker para instalar de verdade).</summary>
    public const string SimulatedNetworkKey = "setup.simulation.network";

    public HardwareCatalog Catalog => HardwareCatalog.Embedded;

    public async Task<SetupStatus> StatusAsync(CancellationToken ct)
    {
        var simulation = await settings.GetAsync<string?>(SimulationKey, null, ct);
        SystemSnapshot system;
        if (simulation is not null && SetupScenarios.All.TryGetValue(simulation, out var scenario))
        {
            system = scenario;
            if (await settings.GetAsync(SimulatedNetworkKey, false, ct))
                system = system with { LdnNetwork = new LdnNetworkConfig(true, true) };
        }
        else
        {
            system = probe.Read();
            if (system.Os == "linux")
                system = system with { BundledWorker = bundled.Status() };
            if (system.Os == "linux" && worker.Install is { } install)
            {
                // O que o worker traz embutido não precisa estar instalado no sistema.
                var missing = system.MissingTools?.Where(t => !(install.BundledTools && WorkerLocator.BundledTools.Contains(t))).ToList();
                system = system with { Worker = install, MissingTools = missing };
            }
        }
        var state = await settings.GetAsync(StateKey, new SetupState(), ct);
        var advice = RadioAdvisor.Advise(system, Catalog);
        var keysValidation = keys.Current();
        var checks = SetupChecks.Evaluate(system, advice, state, keysValidation, Catalog);
        return new SetupStatus(system, advice, state, keysValidation, checks);
    }

    public async Task<SetupState> UpdateStateAsync(SetupStatePatch patch, CancellationToken ct)
    {
        var state = await settings.GetAsync(StateKey, new SetupState(), ct);
        state = state with
        {
            Radio = patch.Radio ?? state.Radio,
            Phy = patch.Phy ?? state.Phy,
            SerialPort = patch.SerialPort ?? state.SerialPort,
            Esp32Board = patch.Esp32Board ?? state.Esp32Board,
            Esp32ModuleConfirmed = patch.Esp32ModuleConfirmed ?? state.Esp32ModuleConfirmed,
            KeysSkipped = patch.KeysSkipped ?? state.KeysSkipped,
            Completed = patch.Completed ?? state.Completed,
        };
        // Trocar de placa ESP32 exige confirmar o módulo de novo.
        if (patch.Esp32Board is not null && patch.Esp32ModuleConfirmed is null)
            state = state with { Esp32ModuleConfirmed = false };
        await settings.SetAsync(StateKey, state, ct);
        return state;
    }

    public async Task ResetAsync(CancellationToken ct) => await settings.SetAsync(StateKey, new SetupState(), ct);

    /// <summary>
    /// Teste real da placa pelo worker (pede a senha de administrador se o worker ainda não estiver
    /// rodando). Na simulação, o resultado segue o status do catálogo. O resultado fica no estado.
    /// </summary>
    public async Task<CardTestResult> RunCardTestAsync(string phy, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        var card = status.Advice.Cards.FirstOrDefault(c => c.Card.Phy == phy)
            ?? throw new ArgumentException("Placa desconhecida.", nameof(phy));

        CardTestResult result;
        if (status.System.SimulatedScenario is not null)
        {
            var passed = card.Status is not (CompatStatus.Problematic or CompatStatus.Unsupported);
            result = new CardTestResult(phy, passed, passed ? "passed" : "tx_failed", time.GetUtcNow(),
                [.. new[] { 1, 6, 11 }.Select(c => new CardTestChannel(c, true, passed ? 5 : 0, passed ? 12 : 0, null))],
                Simulated: true);
        }
        else
        {
            result = ParseCardTest(phy, await worker.CardTestAsync(phy, ct), time.GetUtcNow());
        }
        result = result with { Driver = card.Card.Module ?? card.Card.Driver };

        var state = await settings.GetAsync(StateKey, new SetupState(), ct);
        await settings.SetAsync(StateKey, state with { Phy = phy, CardTest = result }, ct);
        return result;
    }

    /// <summary>Quanto tempo o teste com o Switch procura a sala (para antes, se achar).</summary>
    public const int SwitchTestSeconds = 30;

    /// <summary>Resultados que a simulação (desenvolvimento) sabe produzir.</summary>
    public static readonly string[] SimulatedSwitchOutcomes =
        ["found_decrypted", "found_not_decrypted", "other_session", "not_found", "monitor_failed"];

    /// <summary>
    /// Teste com o Switch pelo worker: procura o anúncio da sala e tenta decifrá-lo com o prod.keys,
    /// sem entrar nela. Na simulação, o resultado é o pedido em <paramref name="simulatedOutcome"/>.
    /// O resultado fica no estado, amarrado à placa e ao prod.keys testados.
    /// </summary>
    public async Task<SwitchTestResult> RunSwitchTestAsync(string phy, string? simulatedOutcome, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        var card = status.Advice.Cards.FirstOrDefault(c => c.Card.Phy == phy)
            ?? throw new ArgumentException("Placa desconhecida.", nameof(phy));
        if (status.Keys is not { Valid: true } validKeys)
            throw new ProdKeysException("keys_missing");

        SwitchTestResult result;
        if (status.System.SimulatedScenario is not null)
        {
            result = SimulateSwitchTest(phy, simulatedOutcome ?? "found_decrypted", time.GetUtcNow());
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(keys.FilePath, ct);
            try
            {
                result = ParseSwitchTest(phy, await worker.BeaconTestAsync(phy, bytes, SwitchTestSeconds, ct), time.GetUtcNow());
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        result = result with { Driver = card.Card.Module ?? card.Card.Driver, KeysSavedAt = validKeys.SavedAt };

        // Cancelado não apaga um resultado anterior: só não conta como teste novo.
        if (result.Code != "cancelled")
        {
            var state = await settings.GetAsync(StateKey, new SetupState(), ct);
            await settings.SetAsync(StateKey, state with { Phy = phy, SwitchTest = result }, ct);
        }
        return result;
    }

    public static SwitchTestResult SimulateSwitchTest(string phy, string outcome, DateTimeOffset at)
    {
        if (!SimulatedSwitchOutcomes.Contains(outcome))
            throw new ArgumentException("Resultado desconhecido.", nameof(outcome));
        var channels = new[] { 1, 6, 11, 36, 40, 44, 48 }.Select(c => new SwitchTestChannel(c, outcome != "monitor_failed", null)).ToList();
        var networks = outcome switch
        {
            "found_decrypted" => [new SwitchTestNetwork("0x0100610011000000", 6, 3, true, 1, 2)],
            "other_session" => [new SwitchTestNetwork("0x0100abcd00000000", 1, 3, false, 2, 8)],
            _ => new List<SwitchTestNetwork>(),
        };
        return new SwitchTestResult(phy, outcome == "found_decrypted", outcome, at, networks, outcome == "monitor_failed" ? [] : channels,
            Undecryptable: outcome == "found_not_decrypted" ? 4 : 0,
            UndecryptableChannels: outcome == "found_not_decrypted" ? [6] : [],
            Sweeps: outcome == "found_decrypted" ? 1 : outcome == "found_not_decrypted" ? 2 : 7,
            Seconds: outcome switch { "found_decrypted" => 2.4, "found_not_decrypted" => 8.4, "monitor_failed" => 0.2, _ => 30 },
            Error: outcome == "monitor_failed" ? "OSError: [Errno 95] Operation not supported" : null,
            Simulated: true);
    }

    public static SwitchTestResult ParseSwitchTest(string phy, JsonObject json, DateTimeOffset at)
    {
        var networks = (json["networks"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(n => new SwitchTestNetwork(
                n["commId"]?.GetValue<string>() ?? "?",
                n["channel"]?.GetValue<int>() ?? 0,
                n["protocol"]?.GetValue<int>() ?? 0,
                n["frlg"]?.GetValue<bool>() ?? false,
                n["participants"]?.GetValue<int>() ?? 0,
                n["maxParticipants"]?.GetValue<int>() ?? 0))
            .ToList();
        var channels = (json["channels"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(c => new SwitchTestChannel(c["channel"]?.GetValue<int>() ?? 0, c["usable"]?.GetValue<bool>() ?? false, c["error"]?.GetValue<string>()))
            .ToList();
        var undecryptableChannels = (json["undecryptableChannels"] as JsonArray ?? []).Select(c => c?.GetValue<int>() ?? 0).ToList();
        return new SwitchTestResult(phy, json["passed"]?.GetValue<bool>() ?? false, json["code"]?.GetValue<string>() ?? "unknown", at,
            networks, channels,
            Undecryptable: json["undecryptable"]?.GetValue<int>() ?? 0,
            UndecryptableChannels: undecryptableChannels,
            Sweeps: json["sweeps"]?.GetValue<int>() ?? 0,
            Seconds: json["seconds"]?.GetValue<double>() ?? 0,
            Error: json["error"]?.GetValue<string>());
    }

    public static CardTestResult ParseCardTest(string phy, JsonObject json, DateTimeOffset at)
    {
        var channels = (json["channels"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(c => new CardTestChannel(
                c["channel"]?.GetValue<int>() ?? 0,
                c["set"]?.GetValue<bool>() ?? false,
                c["sent"]?.GetValue<int>() ?? 0,
                c["received"]?.GetValue<int>() ?? 0,
                c["error"]?.GetValue<string>()))
            .ToList();
        return new CardTestResult(phy, json["passed"]?.GetValue<bool>() ?? false,
            json["code"]?.GetValue<string>() ?? "unknown", at, channels, json["error"]?.GetValue<string>());
    }

    /// <summary>Instala a configuração do NetworkManager e do udev para as interfaces do LDN (via worker).</summary>
    public async Task InstallNetworkConfigAsync(CancellationToken ct)
    {
        if (await SimulationAsync(ct) is not null)
        {
            await settings.SetAsync(SimulatedNetworkKey, true, ct);
            return;
        }
        await worker.InstallNetworkConfigAsync(ct);
    }

    public Task<string?> SimulationAsync(CancellationToken ct) => settings.GetAsync<string?>(SimulationKey, null, ct);

    public async Task SetSimulationAsync(string? scenario, CancellationToken ct)
    {
        if (scenario is not null && !SetupScenarios.All.ContainsKey(scenario))
            throw new ArgumentException("Cenário desconhecido.", nameof(scenario));
        await settings.SetAsync(SimulationKey, scenario, ct);
        await settings.SetAsync(SimulatedNetworkKey, false, ct);
    }
}
