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
public sealed class SetupService(SettingsStore settings, ProdKeys keys, ISystemProbe probe)
{
    public const string StateKey = "setup.state";
    public const string SimulationKey = "setup.simulation";

    public HardwareCatalog Catalog => HardwareCatalog.Embedded;

    public async Task<SetupStatus> StatusAsync(CancellationToken ct)
    {
        var simulation = await settings.GetAsync<string?>(SimulationKey, null, ct);
        var system = simulation is not null && SetupScenarios.All.TryGetValue(simulation, out var scenario)
            ? scenario
            : probe.Read();
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

    public Task<string?> SimulationAsync(CancellationToken ct) => settings.GetAsync<string?>(SimulationKey, null, ct);

    public async Task SetSimulationAsync(string? scenario, CancellationToken ct)
    {
        if (scenario is not null && !SetupScenarios.All.ContainsKey(scenario))
            throw new ArgumentException("Cenário desconhecido.", nameof(scenario));
        await settings.SetAsync(SimulationKey, scenario, ct);
    }
}
