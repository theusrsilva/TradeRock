using TradeRock.Core.Activities;
using TradeRock.Core.Engines.Pokeldn;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Engines;

public sealed class FakeEngineOptions
{
    /// <summary>Multiplicador dos atrasos. 1 = ritmo realista; 0 = instantâneo (testes).</summary>
    public double SpeedFactor { get; set; } = 1.0;

    /// <summary>Pokémon que o "Switch" simulado manda na troca <c>i</c> (0-based) do pedido.</summary>
    public Func<TradeRequest, int, byte[]> HostPokemon { get; set; } =
        (_, _) => throw new InvalidOperationException("HostPokemon não configurado.");
}

/// <summary>
/// Simula uma troca completa, sem Switch, placa nem prod.keys. Segue a mesma sequência de marcos
/// que o frlg-ldn-trade registra (transport.py, sim.py, trade.py), com atrasos no ritmo de uma
/// troca real, e cenários de falha para testar o assistente e o coordenador.
/// </summary>
public sealed class FakeEngine(FakeEngineOptions options, TimeProvider? timeProvider = null) : ITradeEngine, IActivityEngine
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // Tentativas feitas para o pedido atual (o cenário ConnectFailuresThenSuccess precisa contar).
    private readonly Lock _lock = new();
    private TradeRequest? _lastRequest;
    private int _attempts;

    public string Id => "fake";
    public bool IsSimulation => true;

    public Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new EngineAvailability(true));

    /// <summary>A simulação vale para todos os jogos do catálogo.</summary>
    public bool Supports(GameFamily family) => true;

    public async Task<TradeAttemptResult> RunAttemptAsync(
        TradeRequest request, Func<TradeEvent, ValueTask> emit, CancellationToken cancellationToken)
    {
        int attempt;
        lock (_lock)
        {
            if (!ReferenceEquals(_lastRequest, request))
            {
                _lastRequest = request;
                _attempts = 0;
            }
            attempt = ++_attempts;
        }

        var scenario = request.Simulation?.Scenario ?? SimulationScenario.Success;
        var ct = cancellationToken;

        ValueTask Emit(TradeEventKind kind, string message, int? tradeIndex = null, byte[]? data = null) =>
            emit(new TradeEvent(kind, _time.GetUtcNow(), message) { TradeIndex = tradeIndex, ReceivedData = data });

        try
        {
            await Emit(TradeEventKind.Scanning, "Procurando a sala de troca do Switch (canais 1, 6 e 11)...");
            await Wait(1.5, ct);

            if (scenario == SimulationScenario.NoNetworkFound)
            {
                await Wait(1.5, ct);
                return TradeAttemptResult.Fail("no_network_found",
                    "Nenhuma sala de troca encontrada (simulação).", retryable: true);
            }

            await Emit(TradeEventKind.BeaconFound, "Sala de troca encontrada.");
            await Wait(0.5, ct);

            if (scenario == SimulationScenario.WrongKeys)
            {
                return TradeAttemptResult.Fail("beacon_not_decrypted",
                    "A sala foi encontrada, mas não deu para decifrá-la: as chaves não conferem (simulação).",
                    retryable: false);
            }

            await Emit(TradeEventKind.BeaconDecrypted, "Sala decifrada com as chaves.");
            await Wait(0.3, ct);

            if (scenario == SimulationScenario.ConnectFailuresThenSuccess
                && attempt <= (request.Simulation?.FailuresBeforeSuccess ?? 0))
            {
                await Emit(TradeEventKind.Log, "Connect timed out in the kernel (simulação).");
                await Wait(1.0, ct);
                return TradeAttemptResult.Fail("join_failed",
                    "A conexão com o Switch não completou (simulação).", retryable: true);
            }

            await Emit(TradeEventKind.JoinRequested, $"Pedido de entrada enviado como \"{request.Trainer.Name}\".");

            if (scenario == SimulationScenario.ApprovalNeverGiven)
            {
                // O coordenador encerra pelo timeout da fase de aprovação.
                await Task.Delay(Timeout.InfiniteTimeSpan, _time, ct);
            }

            await Wait(4.0, ct);

            if (scenario == SimulationScenario.JoinRejected)
            {
                return TradeAttemptResult.Fail("join_rejected",
                    "O Switch recusou o pedido de entrada (simulação).", retryable: false);
            }

            await Emit(TradeEventKind.Joined, "Entrou na sala. Conexão confirmada pelo Switch.");
            await Wait(3.0, ct);
            await Emit(TradeEventKind.InRoom, "O jogador entrou na sala de troca.");
            await Wait(3.0, ct);
            await Emit(TradeEventKind.HostSat, "O jogador sentou. Sentando na outra cadeira.");
            await Wait(1.5, ct);
            await Emit(TradeEventKind.CardsExchanged, "Cartões de treinador trocados.");
            await Wait(1.5, ct);
            await Emit(TradeEventKind.TradeMenuOpen, "Menu de troca aberto; time do Switch recebido.");

            for (var i = 0; i < request.Trades; i++)
            {
                await Wait(1.0, ct);
                var offered = request.Party[request.OfferedSlots[i]];
                await Emit(TradeEventKind.TradeOffered, $"Oferecendo {offered.DisplayName}; aguardando o Switch.", i);
                await Wait(4.0, ct);

                if (scenario == SimulationScenario.HostCancels && i == 0)
                {
                    await Emit(TradeEventKind.TradeCancelled, "O Switch cancelou a troca.", i);
                    await Wait(2.0, ct);
                    await Emit(TradeEventKind.Leaving, "Saindo da sala.");
                    await Wait(2.0, ct);
                    await Emit(TradeEventKind.LinkClosed, "Conexão encerrada.");
                    return TradeAttemptResult.Fail("host_cancelled", "A troca foi cancelada no Switch.", retryable: false);
                }

                await Emit(TradeEventKind.Confirming, "Confirmando a troca...", i);
                await Wait(2.0, ct);

                if (scenario == SimulationScenario.ConnectionLostMidTrade && i == 0)
                {
                    await Wait(1.0, ct);
                    return TradeAttemptResult.Fail("connection_lost",
                        "A conexão caiu durante a troca (simulação).", retryable: false);
                }

                await Wait(6.0, ct); // animação da troca
                await Emit(TradeEventKind.TradeDone, "Troca confirmada.", i, options.HostPokemon(request, i));
            }

            await Wait(2.0, ct);
            await Emit(TradeEventKind.Leaving, "Todas as trocas concluídas. Aguardando o jogador sair da sala.");
            await Wait(3.0, ct);
            await Emit(TradeEventKind.LinkClosed, "Conexão encerrada.");
            return TradeAttemptResult.Completed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // No motor real, aqui o rádio volta ao estado anterior (interfaces removidas etc.).
            await Emit(TradeEventKind.Log, "Motor encerrado (simulação).");
            return TradeAttemptResult.Cancelled;
        }
    }

    private Task Wait(double seconds, CancellationToken ct)
    {
        var scaled = TimeSpan.FromSeconds(seconds * options.SpeedFactor);
        return scaled <= TimeSpan.Zero ? YieldAsync(ct) : Task.Delay(scaled, _time, ct);
    }

    private static async Task YieldAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
    }

    public bool SupportsActivity(GameFamily family, ActivityKind kind) => PokeldnActivity.Supports(family, kind);

    /// <summary>Mystery Gift simulado (modo demonstração): os mesmos marcos do pokeldn, sem console.</summary>
    public async Task<ActivityResult> RunActivityAsync(ActivityRequest request, Func<ActivityEvent, ValueTask> emit, CancellationToken ct)
    {
        try
        {
            await emit(new ActivityEvent(ActivityEventKind.Started, _time.GetUtcNow()));
            await Wait(1, ct);
            if (request.GameInfo.Family == GameFamily.Swsh)
            {
                // O console só ouve o anúncio: fica no ar o tempo do pokeldn, ou até a pessoa encerrar.
                await emit(new ActivityEvent(ActivityEventKind.Advertising, _time.GetUtcNow()));
                await Task.Delay(TimeSpan.FromSeconds(PokeldnActivity.GiftSeconds), _time, ct);
                return ActivityResult.Done;
            }
            await emit(new ActivityEvent(ActivityEventKind.Waiting, _time.GetUtcNow()));
            await Wait(3, ct);
            await emit(new ActivityEvent(ActivityEventKind.Joined, _time.GetUtcNow()));
            await Wait(4, ct);
            await emit(new ActivityEvent(ActivityEventKind.Delivered, _time.GetUtcNow()));
            return ActivityResult.Done;
        }
        catch (OperationCanceledException)
        {
            return request.GameInfo.Family == GameFamily.Swsh ? ActivityResult.Done : ActivityResult.Cancelled;
        }
    }
}
