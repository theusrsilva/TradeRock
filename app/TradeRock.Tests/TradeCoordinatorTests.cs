using Microsoft.Extensions.Logging.Abstractions;
using TradeRock.Core.Games;
using TradeRock.Core.Engines;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.Tests;

public sealed class TrainerNameTests
{
    [Theory]
    [InlineData("EMU")]
    [InlineData("Rocha")]
    [InlineData("A.B-C!?")]
    [InlineData("RED♂")]
    public void Nomes_validos(string name) => Assert.Empty(TrainerName.Validate(name));

    [Theory]
    [InlineData("", "trainer_name_empty")]
    [InlineData("Matheus8", "trainer_name_too_long")]
    [InlineData("João", "trainer_name_invalid_chars")]
    [InlineData("Ash_1", "trainer_name_invalid_chars")]
    public void Nomes_invalidos(string name, string code) => Assert.Contains(code, TrainerName.Validate(name));
}

public sealed class TradeRequestTests
{
    [Fact]
    public void Time_de_um_so_pokemon_e_recusado()
    {
        var request = TestTrades.Request(partySize: 1, offered: [0]);
        Assert.Contains("party_size", request.Validate());
    }

    [Fact]
    public void Mais_trocas_que_pokemon_e_posicao_repetida_sao_recusadas()
    {
        Assert.Contains("trades_exceed_party", TestTrades.Request(partySize: 2, offered: [0, 1, 0]).Validate());
        Assert.Contains("offered_slots_repeated", TestTrades.Request(partySize: 3, offered: [1, 1]).Validate());
        Assert.Contains("offered_slot_out_of_range", TestTrades.Request(partySize: 2, offered: [2]).Validate());
    }

    [Fact]
    public void Seis_trocas_com_seis_pokemon_e_valido()
    {
        Assert.Empty(TestTrades.Request(partySize: 6, offered: [0, 1, 2, 3, 4, 5]).Validate());
    }
}

public sealed class TradeCoordinatorTests
{
    private static readonly TradeSettings Fast = new() { RetryDelaySeconds = 0, MaxAttempts = 5 };

    [Fact]
    public async Task Troca_simples_completa_grava_recebido_e_historico()
    {
        var (coordinator, recorder) = TestTrades.Coordinator();

        coordinator.Start(TestTrades.FastFake(), TestTrades.Request(), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        var s = coordinator.Current!;
        Assert.Equal(TradeSessionStatus.Completed, s.Status);
        Assert.Equal(1, s.TradesCompleted);
        Assert.NotNull(s.ReceivedPokemonIds[0]);

        var record = Assert.Single(recorder.Records);
        Assert.Equal(TradeSessionStatus.Completed, record.Status);
        var entry = Assert.Single(record.Entries);
        Assert.Equal(s.ReceivedPokemonIds[0], entry.ReceivedPokemonId);
        Assert.Contains("\"kind\"", record.EventsJson);
    }

    [Fact]
    public async Task Eventos_seguem_a_ordem_do_motor_real()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        coordinator.Start(TestTrades.FastFake(), TestTrades.Request(), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        var kinds = TestTrades.Kinds(coordinator);
        TradeEventKind[] expected =
        [
            TradeEventKind.SessionStarted, TradeEventKind.AttemptStarted, TradeEventKind.Scanning,
            TradeEventKind.BeaconFound, TradeEventKind.BeaconDecrypted, TradeEventKind.JoinRequested,
            TradeEventKind.Joined, TradeEventKind.InRoom, TradeEventKind.HostSat, TradeEventKind.CardsExchanged,
            TradeEventKind.TradeMenuOpen, TradeEventKind.TradeOffered, TradeEventKind.Confirming,
            TradeEventKind.TradeDone, TradeEventKind.Leaving, TradeEventKind.LinkClosed, TradeEventKind.SessionFinished,
        ];
        Assert.Equal(expected, kinds);
    }

    [Fact]
    public async Task Seis_trocas_recebem_seis_pokemon()
    {
        var (coordinator, recorder) = TestTrades.Coordinator();
        coordinator.Start(TestTrades.FastFake(), TestTrades.Request(partySize: 6, offered: [5, 4, 3, 2, 1, 0]), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        var s = coordinator.Current!;
        Assert.Equal(TradeSessionStatus.Completed, s.Status);
        Assert.Equal(6, s.TradesCompleted);
        Assert.All(s.ReceivedPokemonIds, id => Assert.NotNull(id));
        Assert.Equal(6, TestTrades.Kinds(coordinator).Count(k => k == TradeEventKind.TradeDone));
        Assert.Equal([0, 1, 2, 3, 4, 5], recorder.Records[0].Entries.Select(e => e.TradeIndex));
    }

    [Fact]
    public async Task Falhas_de_conexao_sao_repetidas_ate_conseguir()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        var request = TestTrades.Request(simulation: new SimulationOptions(SimulationScenario.ConnectFailuresThenSuccess, 2));

        coordinator.Start(TestTrades.FastFake(), request, Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        var s = coordinator.Current!;
        Assert.Equal(TradeSessionStatus.Completed, s.Status);
        Assert.Equal(3, s.Attempt);
        Assert.Equal(2, TestTrades.Kinds(coordinator).Count(k => k == TradeEventKind.RetryScheduled));
    }

    [Fact]
    public async Task Para_depois_do_maximo_de_tentativas()
    {
        var (coordinator, recorder) = TestTrades.Coordinator();
        var request = TestTrades.Request(simulation: new SimulationOptions(SimulationScenario.ConnectFailuresThenSuccess, 10));

        coordinator.Start(TestTrades.FastFake(), request, Fast with { MaxAttempts = 3 });
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        var s = coordinator.Current!;
        Assert.Equal(TradeSessionStatus.Failed, s.Status);
        Assert.Equal("join_failed", s.FailureCode);
        Assert.Equal(3, s.Attempt);
        Assert.Equal(3, recorder.Records[0].Attempts);
    }

    [Theory]
    [InlineData(SimulationScenario.WrongKeys, "beacon_not_decrypted")]
    [InlineData(SimulationScenario.JoinRejected, "join_rejected")]
    [InlineData(SimulationScenario.HostCancels, "host_cancelled")]
    [InlineData(SimulationScenario.ConnectionLostMidTrade, "connection_lost")]
    public async Task Falhas_definitivas_nao_sao_repetidas(SimulationScenario scenario, string code)
    {
        var (coordinator, _) = TestTrades.Coordinator();
        coordinator.Start(TestTrades.FastFake(), TestTrades.Request(simulation: new SimulationOptions(scenario)), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        var s = coordinator.Current!;
        Assert.Equal(TradeSessionStatus.Failed, s.Status);
        Assert.Equal(code, s.FailureCode);
        Assert.Equal(1, s.Attempt);
    }

    [Fact]
    public async Task Sem_sala_encontrada_repete()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        var request = TestTrades.Request(simulation: new SimulationOptions(SimulationScenario.NoNetworkFound));
        coordinator.Start(TestTrades.FastFake(), request, Fast with { MaxAttempts = 2 });
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        Assert.Equal("no_network_found", coordinator.Current!.FailureCode);
        Assert.Equal(2, coordinator.Current.Attempt);
    }

    [Fact]
    public async Task Nunca_repete_depois_de_entrar_na_sala()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        var engine = new ScriptedEngine(async emit =>
        {
            await emit(new TradeEvent(TradeEventKind.Joined, DateTimeOffset.UtcNow));
            return TradeAttemptResult.Fail("weird", "falha que o motor acha repetível", retryable: true);
        });

        coordinator.Start(engine, TestTrades.Request(), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        Assert.Equal(1, engine.Calls);
        Assert.Equal(TradeSessionStatus.Failed, coordinator.Current!.Status);
    }

    [Fact]
    public async Task Troca_confirmada_antes_de_uma_falha_vira_parcial()
    {
        var (coordinator, recorder) = TestTrades.Coordinator();
        var engine = new ScriptedEngine(async emit =>
        {
            await emit(new TradeEvent(TradeEventKind.Joined, DateTimeOffset.UtcNow));
            await emit(new TradeEvent(TradeEventKind.TradeDone, DateTimeOffset.UtcNow)
                { TradeIndex = 0, ReceivedData = DemoPokemon.All[1].Data });
            return TradeAttemptResult.Fail("connection_lost", "caiu", retryable: false);
        });

        coordinator.Start(engine, TestTrades.Request(partySize: 3, offered: [0, 1]), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        var s = coordinator.Current!;
        Assert.Equal(TradeSessionStatus.PartiallyCompleted, s.Status);
        Assert.Equal(1, s.TradesCompleted);
        Assert.Single(recorder.Stored);
        Assert.Null(recorder.Records[0].Entries[1].ReceivedPokemonId);
    }

    [Fact]
    public async Task Motor_que_lanca_excecao_vira_falha_e_nao_derruba_o_app()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        var engine = new ScriptedEngine(_ => throw new InvalidOperationException("boom"));

        coordinator.Start(engine, TestTrades.Request(), Fast with { MaxAttempts = 2 });
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        Assert.Equal("engine_crashed", coordinator.Current!.FailureCode);
        Assert.Equal(2, engine.Calls);
    }

    [Fact]
    public async Task Aprovacao_nao_dada_estoura_o_tempo_da_fase()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        var request = TestTrades.Request(simulation: new SimulationOptions(SimulationScenario.ApprovalNeverGiven));

        coordinator.Start(TestTrades.FastFake(), request, Fast with { MaxAttempts = 1, ApprovalTimeoutSeconds = 1 });
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        Assert.Equal("approval_timeout", coordinator.Current!.FailureCode);
    }

    [Fact]
    public async Task Cancelar_encerra_o_motor_com_seguranca()
    {
        var (coordinator, recorder) = TestTrades.Coordinator();
        var request = TestTrades.Request(simulation: new SimulationOptions(SimulationScenario.ApprovalNeverGiven));
        coordinator.Start(TestTrades.FastFake(), request, Fast);

        await TestTrades.WaitForAsync(() => TestTrades.Kinds(coordinator).Contains(TradeEventKind.JoinRequested));
        await coordinator.CancelAsync(TimeSpan.FromSeconds(5));

        var s = coordinator.Current!;
        Assert.Equal(TradeSessionStatus.Cancelled, s.Status);
        Assert.Equal("cancelled", s.FailureCode);
        Assert.False(coordinator.IsBusy);
        Assert.Contains(TestTrades.Events(coordinator), e => e.Kind == TradeEventKind.Log && e.Message!.Contains("encerrado"));
        Assert.Equal(TradeSessionStatus.Cancelled, recorder.Records[0].Status);
    }

    [Fact]
    public async Task So_uma_troca_por_vez()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        var slow = TestTrades.Request(simulation: new SimulationOptions(SimulationScenario.ApprovalNeverGiven));
        coordinator.Start(TestTrades.FastFake(), slow, Fast);

        Assert.Throws<TradeBusyException>(() => coordinator.Start(TestTrades.FastFake(), TestTrades.Request(), Fast));

        await coordinator.CancelAsync(TimeSpan.FromSeconds(5));
        coordinator.Start(TestTrades.FastFake(), TestTrades.Request(), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);
        Assert.Equal(TradeSessionStatus.Completed, coordinator.Current!.Status);
    }

    [Fact]
    public void Pedido_invalido_nao_inicia()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        var request = TestTrades.Request() with { Trainer = new TrainerProfile("João", 1, null) };

        var ex = Assert.Throws<InvalidTradeRequestException>(() => coordinator.Start(TestTrades.FastFake(), request, Fast));
        Assert.Contains("trainer_name_invalid_chars", ex.Errors);
        Assert.Null(coordinator.Current);
    }

    [Fact]
    public async Task Inscrito_atrasado_recebe_a_sessao_desde_o_inicio_e_retoma_pelo_seq()
    {
        var (coordinator, _) = TestTrades.Coordinator();
        coordinator.Start(TestTrades.FastFake(), TestTrades.Request(), Fast);
        await coordinator.Completion.WaitAsync(TestTrades.Timeout);

        using var all = coordinator.Subscribe();
        var replayed = new List<TradeEvent>();
        while (all.Reader.TryRead(out var e))
            replayed.Add(e);
        Assert.Equal(TradeEventKind.SessionStarted, replayed[0].Kind);
        Assert.Equal(TradeEventKind.SessionFinished, replayed[^1].Kind);
        Assert.True(replayed.Zip(replayed.Skip(1)).All(p => p.First.Seq < p.Second.Seq));

        using var partial = coordinator.Subscribe(afterSeq: replayed[^3].Seq);
        var count = 0;
        while (partial.Reader.TryRead(out _))
            count++;
        Assert.Equal(2, count);
    }
}

/// <summary>Motor roteirizado para testar regras do coordenador.</summary>
internal sealed class ScriptedEngine(Func<Func<TradeEvent, ValueTask>, Task<TradeAttemptResult>> script) : ITradeEngine
{
    public int Calls { get; private set; }
    public string Id => "scripted";
    public bool IsSimulation => true;

    public Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken ct) => Task.FromResult(new EngineAvailability(true));

    public Task<TradeAttemptResult> RunAttemptAsync(TradeRequest request, Func<TradeEvent, ValueTask> emit, CancellationToken ct)
    {
        Calls++;
        return script(emit);
    }
}

internal sealed class MemoryRecorder : ITradeRecorder
{
    public List<byte[]> Stored { get; } = [];
    public List<TradeRecord> Records { get; } = [];

    public Task<Guid> StoreReceivedAsync(Guid owner, byte[] data, GameFamily family, CancellationToken ct)
    {
        lock (Stored) Stored.Add(data);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task SaveAsync(Guid owner, TradeRecord record, CancellationToken ct)
    {
        lock (Records) Records.Add(record);
        return Task.CompletedTask;
    }
}

internal static class TestTrades
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static (TradeCoordinator, MemoryRecorder) Coordinator()
    {
        var recorder = new MemoryRecorder();
        return (new TradeCoordinator(recorder, TimeProvider.System, NullLogger<TradeCoordinator>.Instance), recorder);
    }

    public static FakeEngine FastFake() => new(new FakeEngineOptions
    {
        SpeedFactor = 0,
        HostPokemon = (_, i) => DemoPokemon.All[i % DemoPokemon.All.Count].Data,
    });

    public static TradeRequest Request(int partySize = 2, int[]? offered = null, SimulationOptions? simulation = null)
    {
        var party = Enumerable.Range(0, partySize)
            .Select(i => new PartyMember(Guid.NewGuid(), DemoPokemon.All[i % DemoPokemon.All.Count].Data, $"Mon{i}"))
            .ToList();
        return new TradeRequest(party, offered ?? [partySize > 1 ? 1 : 0], TradeGame.LeafGreen, TrainerProfile.Default, simulation);
    }

    public static List<TradeEvent> Events(TradeCoordinator coordinator)
    {
        using var sub = coordinator.Subscribe();
        var list = new List<TradeEvent>();
        while (sub.Reader.TryRead(out var e))
            list.Add(e);
        return list;
    }

    public static List<TradeEventKind> Kinds(TradeCoordinator coordinator) =>
        Events(coordinator).Where(e => e.Kind != TradeEventKind.Log).Select(e => e.Kind).ToList();

    public static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condição não atingida.");
            await Task.Delay(10);
        }
    }
}
