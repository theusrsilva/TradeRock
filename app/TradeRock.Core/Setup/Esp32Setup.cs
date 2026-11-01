using Microsoft.Extensions.Logging;
using TradeRock.Core.Esp32;
using TradeRock.Core.Storage;

namespace TradeRock.Core.Setup;

/// <summary>Gravação do firmware em andamento (o app acompanha pela API).</summary>
public sealed record Esp32FlashJob(
    Guid Id,
    string Port,
    string Stage, // identify | write | verify | probe | done | error
    int Done,
    int Total,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    Esp32FirmwareResult? Result = null)
{
    public bool Finished => Stage is "done" or "error";
}

/// <summary>
/// ESP32 no assistente: firmware (LDN_HELLO, esptool) e teste com o Switch. Uma operação por vez na
/// porta: o firmware e o esptool não podem disputar a serial. Na simulação (desenvolvimento), os
/// resultados seguem o catálogo, sem tocar em porta nenhuma.
/// </summary>
public sealed class Esp32Setup(SetupService setup, SettingsStore settings, IEsp32Link link, IEsp32Flasher flasher, ProdKeys keys, TimeProvider time,
    ILogger<Esp32Setup> logger)
{
    private readonly SemaphoreSlim _port = new(1, 1);
    private Esp32FlashJob? _job;

    /// <summary>Quanto o teste com o Switch procura a sala (o mesmo da placa Wi-Fi).</summary>
    public static TimeSpan SwitchTestLimit => TimeSpan.FromSeconds(SetupService.SwitchTestSeconds);

    /// <summary>Espera o firmware iniciar depois do reset do esptool, antes do LDN_HELLO.</summary>
    public TimeSpan BootWait { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Novas conferências depois da gravação, com <see cref="BootWait"/> entre elas, enquanto a placa volta.</summary>
    public int BootRetries { get; init; } = 6;

    public Esp32FlashJob? CurrentJob => _job;

    private async Task<(SetupStatus Status, string Port)> ContextAsync(CancellationToken ct)
    {
        var status = await setup.StatusAsync(ct);
        var port = status.State.SerialPort;
        if (port is null || status.System.SerialPorts.All(p => p.Path != port))
            throw new Esp32Exception("serial_not_chosen", "Escolha a porta do ESP32.");
        return (status, port);
    }

    private async Task<T> ExclusiveAsync<T>(Func<Task<T>> body, CancellationToken ct)
    {
        if (!await _port.WaitAsync(TimeSpan.Zero, ct))
            throw new Esp32Exception("esp32_busy", "Outra operação está usando o ESP32.");
        try
        {
            return await body();
        }
        catch (Esp32Exception e)
        {
            logger.LogWarning("ESP32: {Code}. {Message}", e.Code, e.Message);
            throw;
        }
        finally
        {
            _port.Release();
        }
    }

    private async Task SaveAsync(Func<SetupState, SetupState> change, CancellationToken ct)
    {
        var state = await settings.GetAsync(SetupService.StateKey, new SetupState(), ct);
        await settings.SetAsync(SetupService.StateKey, change(state), ct);
    }

    private static string? SimulatedTarget(SetupStatus status) =>
        status.System.SimulatedScenario is null ? null
            : Board(status)?.Chip ?? "esp32s3";

    private static Esp32Entry? Board(SetupStatus status) =>
        HardwareCatalog.Embedded.Esp32.FirstOrDefault(b => b.Id == status.State.Esp32Board);

    /// <summary>LDN_HELLO na porta escolhida: rápido e sem reiniciar a placa.</summary>
    public Task<Esp32ProbeResult> ProbeAsync(CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var (status, port) = await ContextAsync(ct);
        var result = await ProbeCoreAsync(status, port, ct);
        await SaveAsync(s => s with { Esp32Probe = result }, ct);
        return result;
    }, ct);

    private async Task<Esp32ProbeResult> ProbeCoreAsync(SetupStatus status, string port, CancellationToken ct)
    {
        if (SimulatedTarget(status) is { } simulated)
        {
            // Simulação: sem firmware até "gravar"; depois responde com o chip da placa escolhida.
            var flashed = status.State.Esp32Firmware is { Written: true } fw && fw.Port == port;
            return flashed
                ? new Esp32ProbeResult(port, true, "hello_ok", simulated, time.GetUtcNow(), Simulated: true)
                : new Esp32ProbeResult(port, false, "esp32_firmware_missing", null, time.GetUtcNow(), Simulated: true);
        }
        try
        {
            var hello = await Task.Run(() => link.Probe(port, ct), ct);
            return new Esp32ProbeResult(port, true, "hello_ok", hello.Target, time.GetUtcNow());
        }
        catch (Esp32Exception e)
        {
            return new Esp32ProbeResult(port, false, e.Code, null, time.GetUtcNow());
        }
    }

    /// <summary>esptool: tipo, revisão e flash do chip (reinicia a placa, que volta a rodar o firmware).</summary>
    public Task<Esp32ChipResult> IdentifyAsync(CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var (status, port) = await ContextAsync(ct);
        var result = await IdentifyCoreAsync(status, port, ct);
        await SaveAsync(s => s with { Esp32Chip = result }, ct);
        return result;
    }, ct);

    private async Task<Esp32ChipResult> IdentifyCoreAsync(SetupStatus status, string port, CancellationToken ct)
    {
        if (SimulatedTarget(status) is { } simulated)
        {
            var image = Esp32Firmware.For(simulated);
            var description = simulated == "esp32" ? "ESP32-D0WD-V3 (revision v3.1)" : $"{simulated.ToUpperInvariant().Replace("ESP32", "ESP32-")} (revision v0.2)";
            return new Esp32ChipResult(port, simulated, description, simulated == "esp32" ? 301 : 2, image?.FlashSize ?? "4MB", time.GetUtcNow(), Simulated: true);
        }
        var chip = await flasher.IdentifyAsync(port, ct);
        return new Esp32ChipResult(port, chip.Target, chip.Description, chip.Revision, chip.FlashSize, time.GetUtcNow());
    }

    /// <summary>Compara a flash com o firmware embutido para o chip (verify-flash).</summary>
    public Task<Esp32FirmwareResult> VerifyAsync(CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var (status, port) = await ContextAsync(ct);
        var chip = await IdentifyCoreAsync(status, port, ct);
        var image = Esp32Firmware.For(chip.Target) ?? throw new Esp32Exception("esp32_chip_unsupported", chip.Description);
        Esp32FirmwareResult result;
        if (chip.Simulated)
            result = new Esp32FirmwareResult(port, chip.Target, status.State.Esp32Firmware is { Written: true }, false, time.GetUtcNow(), Simulated: true);
        else
        {
            var outcome = await flasher.VerifyAsync(port, image, ct);
            result = new Esp32FirmwareResult(port, image.Target, outcome.Matches, false, time.GetUtcNow());
        }
        await SaveAsync(s => s with { Esp32Chip = chip, Esp32Firmware = result }, ct);
        return result;
    }, ct);

    /// <summary>
    /// Começa a gravar o firmware em segundo plano: identifica o chip, confere a flash, grava, confere de
    /// novo e faz o LDN_HELLO. O andamento fica em <see cref="CurrentJob"/>.
    /// </summary>
    public async Task<Esp32FlashJob> StartFlashAsync(CancellationToken ct)
    {
        var (status, port) = await ContextAsync(ct);
        if (!await _port.WaitAsync(TimeSpan.Zero, ct))
            throw new Esp32Exception("esp32_busy", "Outra operação está usando o ESP32.");
        var job = new Esp32FlashJob(Guid.NewGuid(), port, "identify", 0, 0);
        _job = job;
        _ = Task.Run(async () =>
        {
            try
            {
                await FlashCoreAsync(status, port, job.Id);
            }
            catch (Esp32Exception e)
            {
                logger.LogWarning("Gravação do ESP32: {Code}. {Message}", e.Code, e.Message);
                Update(job.Id, j => j with { Stage = "error", ErrorCode = e.Code, ErrorMessage = e.Message });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Gravação do ESP32 parou com erro.");
                Update(job.Id, j => j with { Stage = "error", ErrorCode = "esp32_error", ErrorMessage = e.Message });
            }
            finally
            {
                _port.Release();
            }
        }, CancellationToken.None);
        return job;
    }

    private void Update(Guid id, Func<Esp32FlashJob, Esp32FlashJob> change)
    {
        if (_job is { } current && current.Id == id)
            _job = change(current);
    }

    private async Task FlashCoreAsync(SetupStatus status, string port, Guid id)
    {
        var ct = CancellationToken.None; // gravar não se interrompe no meio (a placa ficaria sem firmware)
        var chip = await IdentifyCoreAsync(status, port, ct);
        await SaveAsync(s => s with { Esp32Chip = chip }, ct);
        var image = Esp32Firmware.For(chip.Target) ?? throw new Esp32Exception("esp32_chip_unsupported", chip.Description);
        if (chip.FlashSize is { } size && size.EndsWith("MB", StringComparison.Ordinal)
            && int.TryParse(size[..^2], out var mb) && mb < image.FlashSizeMb)
            throw new Esp32Exception("esp32_flash_too_small", $"{size} < {image.FlashSize}");

        Esp32FirmwareResult result;
        if (chip.Simulated)
        {
            for (var i = 1; i <= 4; i++)
                Update(id, j => j with { Stage = "write", Done = i, Total = 4 });
            result = new Esp32FirmwareResult(port, chip.Target, true, true, time.GetUtcNow(), Simulated: true);
        }
        else
        {
            Update(id, j => j with { Stage = "write" });
            var outcome = await flasher.FlashAsync(port, image, p => Update(id, j => j with { Stage = p.Stage, Done = p.Done, Total = p.Total }), ct);
            result = new Esp32FirmwareResult(port, image.Target, outcome.Matches, true, time.GetUtcNow());
            if (!outcome.Matches)
                throw new Esp32Exception("verify_mismatch", "A gravação terminou, mas a conferência não bateu.");
        }
        await SaveAsync(s => s with { Esp32Firmware = result }, ct);

        Update(id, j => j with { Stage = "probe" });
        if (!chip.Simulated)
            await Task.Delay(BootWait, ct);
        var after = await setup.StatusAsync(ct);
        var probe = await ProbeCoreAsync(after, port, ct);
        // A placa reinicia depois da gravação; com o USB nativo (S2, S3, C3, C6) a porta some e volta. Tenta de novo
        // por alguns segundos antes de dizer que ela não respondeu.
        for (var i = 0; i < BootRetries && !probe.Ok && probe.Code is "esp32_no_response" or "serial_port_missing" or "serial_busy"; i++)
        {
            await Task.Delay(BootWait, ct);
            probe = await ProbeCoreAsync(after, port, ct);
        }
        await SaveAsync(s => s with { Esp32Probe = probe }, ct);
        Update(id, j => j with { Stage = "done", Result = result });
    }

    /// <summary>
    /// Teste com o Switch pelo ESP32: escuta a sala nos canais de 2,4 GHz e tenta decifrar o anúncio com
    /// o prod.keys, sem entrar. Mesmos códigos da placa Wi-Fi (D16).
    /// </summary>
    public Task<SwitchTestResult> SwitchTestAsync(string? simulatedOutcome, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var (status, port) = await ContextAsync(ct);
        if (status.Keys is not { Valid: true } validKeys)
            throw new ProdKeysException("keys_missing");
        var probe = status.State.Esp32Probe is { Ok: true } p && p.Port == port ? p
            : throw new Esp32Exception("esp32_firmware_not_checked", "Confira o firmware antes do teste.");

        SwitchTestResult result;
        if (status.System.SimulatedScenario is not null)
            result = SetupService.SimulateSwitchTest(port, simulatedOutcome ?? "found_decrypted", time.GetUtcNow());
        else
        {
            var scan = await Task.Run(() => link.Scan(port, keys.FilePath, SwitchTestLimit, ct), ct);
            result = FromScan(port, scan, time.GetUtcNow());
        }
        result = result with { Radio = "esp32", Driver = probe.Target, KeysSavedAt = validKeys.SavedAt };
        await SaveAsync(s => s with { SwitchTest = result }, ct);
        return result;
    }, ct);

    public static SwitchTestResult FromScan(string port, Esp32ScanResult scan, DateTimeOffset at)
    {
        var code = scan.FoundFrlg ? "found_decrypted"
            : scan.Undecryptable > 0 ? "found_not_decrypted"
            : scan.Rooms.Count > 0 ? "other_session"
            : "not_found";
        var networks = scan.Rooms.Select(r => new SwitchTestNetwork($"0x{r.CommunicationId:x16}", r.Channel, r.Protocol,
            PokeldnEsp32Link.IsPokemon(r.CommunicationId), r.Members, r.Maximum)).ToList();
        var channels = PokeldnEsp32Link.Channels.Select(c => new SwitchTestChannel(c, true, null)).ToList();
        return new SwitchTestResult(port, scan.FoundFrlg, code, at, networks, channels, scan.Undecryptable,
            scan.UndecryptableChannels, scan.Sweeps, Math.Round(scan.Seconds, 1), Radio: "esp32");
    }
}
