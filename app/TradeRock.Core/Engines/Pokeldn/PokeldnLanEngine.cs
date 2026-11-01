using TradeRock.Core.Activities;
using TradeRock.Core.Esp32;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>
/// Modo LAN do Sword/Shield (D24): o pokeldn abre a troca pela rede de casa (UDP), sem ESP32, placa Wi-Fi nem
/// prod.keys, como o usuário, no Linux e no Windows. No console, o modo LAN se liga em Opções com L + R + analógico
/// esquerdo, e o código de troca é ignorado.
/// </summary>
public sealed class PokeldnLanEngine(IPokeldnProcess process) : ITradeEngine, IActivityEngine
{
    public const string EngineId = "lan";

    public string Id => EngineId;

    public bool IsSimulation => false;

    public bool Supports(GameFamily family) => family == GameFamily.Swsh;

    public bool UsesPokeldn(GameFamily family) => true;

    public bool NeedsSetup => false;

    public bool IgnoresLinkCode => true;

    public Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(process.Available ? new EngineAvailability(true) : new EngineAvailability(false, "pokeldn_missing"));

    public async Task<TradeAttemptResult> RunAttemptAsync(TradeRequest request, Func<TradeEvent, ValueTask> emit, CancellationToken cancellationToken)
    {
        if (!Supports(request.GameInfo.Family))
            return TradeAttemptResult.Fail("engine_game_unsupported", "O modo LAN é só do Sword/Shield.", retryable: false);
        var tracker = new PokeldnEngine.Progress(request);
        try
        {
            await foreach (var message in process.RunAsync("trade", PokeldnEngine.Request(request with { LinkCode = null }, "lan", null), cancellationToken))
            {
                if (tracker.Map(message, DateTimeOffset.UtcNow) is { } e)
                    await emit(e);
            }
        }
        catch (Esp32Exception e)
        {
            return TradeAttemptResult.Fail(e.Code, e.Message, retryable: false);
        }
        return tracker.Result(cancellationToken.IsCancellationRequested);
    }

    /// <summary>Pela rede de casa, só as raids do Sword/Shield (o pokeldn tem as ferramentas de raid do modo LAN, D24).</summary>
    public bool SupportsActivity(GameFamily family, ActivityKind kind) => family == GameFamily.Swsh && kind == ActivityKind.Raid;

    public async Task<ActivityResult> RunActivityAsync(ActivityRequest request, Func<ActivityEvent, ValueTask> emit, CancellationToken ct)
    {
        if (!SupportsActivity(request.GameInfo.Family, request.Kind))
            return ActivityResult.Fail("activity_engine_unsupported");
        var progress = new PokeldnActivity.Progress(request);
        try
        {
            await foreach (var message in process.RunAsync("trade", PokeldnActivity.Request(request, "lan", null), ct))
            {
                if (progress.Map(message, DateTimeOffset.UtcNow) is { } e)
                    await emit(e);
            }
        }
        catch (Esp32Exception e)
        {
            return ActivityResult.Fail(e.Code, e.Message);
        }
        return progress.Result(ct.IsCancellationRequested);
    }
}
