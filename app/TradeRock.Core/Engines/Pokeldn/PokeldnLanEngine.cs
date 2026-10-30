using TradeRock.Core.Esp32;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>
/// Modo LAN do Sword/Shield (D24): o pokeldn abre a troca pela rede de casa (UDP), sem ESP32, placa Wi-Fi nem
/// prod.keys, como o usuário, no Linux e no Windows. No console, o modo LAN se liga em Opções com L + R + analógico
/// esquerdo, e o código de troca é ignorado.
/// </summary>
public sealed class PokeldnLanEngine(IPokeldnProcess process) : ITradeEngine
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
        return cancellationToken.IsCancellationRequested ? TradeAttemptResult.Cancelled : tracker.Result();
    }
}
