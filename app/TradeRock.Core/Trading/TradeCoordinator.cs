using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TradeRock.Core.Games;
using TradeRock.Core.Storage;

namespace TradeRock.Core.Trading;

/// <summary>Onde o coordenador guarda o que recebeu e o histórico.</summary>
public interface ITradeRecorder
{
    /// <summary>Guarda o recebido (um Pokémon de <paramref name="family"/>) no cofre de <paramref name="owner"/>.</summary>
    Task<Guid> StoreReceivedAsync(Guid owner, byte[] data, GameFamily family, CancellationToken ct);
    Task SaveAsync(Guid owner, TradeRecord record, CancellationToken ct);
}

public sealed class TradeBusyException() : InvalidOperationException("Já existe uma troca em andamento.");

public sealed class InvalidTradeRequestException(IReadOnlyList<string> errors)
    : ArgumentException("Pedido de troca inválido: " + string.Join(", ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed record TradeSessionSnapshot(
    Guid Id,
    Guid OwnerId,
    string EngineId,
    bool IsSimulation,
    TradeSessionStatus Status,
    int Attempt,
    int MaxAttempts,
    int TradesPlanned,
    int TradesCompleted,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string TrainerName,
    TradeGame Game,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<Guid?> ReceivedPokemonIds,
    /// <summary>O código de troca do pedido, para a tela ao vivo lembrar o que digitar no Switch (D30).</summary>
    string? LinkCode = null);

/// <summary>
/// Conduz uma sessão de troca por vez: tentativas com retry, timeouts por fase, cancelamento seguro,
/// gravação dos recebidos no momento da confirmação e histórico. Os eventos são transmitidos a
/// quem estiver inscrito (SSE), com reprodução dos eventos da sessão atual para quem chega depois.
///
/// Regra de segurança do retry: só se repete uma tentativa que falhou ANTES de entrar na sala
/// (evento Joined). Depois disso, repetir poderia deixar o jogo num estado inconsistente.
/// </summary>
public sealed class TradeCoordinator(ITradeRecorder recorder, TimeProvider time, ILogger<TradeCoordinator> logger)
    : IAsyncDisposable
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();
    private readonly List<Channel<TradeEvent>> _subscribers = [];
    private Session? _session;
    private readonly Dictionary<Guid, Guid> _owners = []; // sessão → dono, para filtrar os eventos por conta
    private long _lastSeq; // global: um cliente SSE que reconecta não perde eventos de uma sessão nova

    public bool IsBusy
    {
        get { lock (_lock) return _session is { Status: TradeSessionStatus.Running }; }
    }

    /// <summary>Sessão atual ou a última encerrada (null se nenhuma nesta execução).</summary>
    public TradeSessionSnapshot? Current
    {
        get { lock (_lock) return _session?.Snapshot(); }
    }

    /// <summary>Tarefa da sessão em andamento (ou da última), para aguardar o fim.</summary>
    public Task Completion
    {
        get { lock (_lock) return _session?.Run ?? Task.CompletedTask; }
    }

    /// <summary>Dono da sessão (das desta execução), ou null se desconhecida.</summary>
    public Guid? OwnerOf(Guid sessionId)
    {
        lock (_lock) return _owners.TryGetValue(sessionId, out var owner) ? owner : null;
    }

    /// <summary>Começa uma sessão. O que for recebido vai para o cofre de <paramref name="owner"/>.</summary>
    public TradeSessionSnapshot Start(ITradeEngine engine, TradeRequest request, TradeSettings settings, Guid? owner = null)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            throw new InvalidTradeRequestException(errors);

        settings = settings.Normalized();
        Session session;
        lock (_lock)
        {
            if (_session is { Status: TradeSessionStatus.Running })
                throw new TradeBusyException();

            session = new Session(Guid.CreateVersion7(), owner ?? OwnerScope.LocalOwner, engine, request, settings, time.GetUtcNow());
            _session = session;
            _owners[session.Id] = session.Owner;
        }

        session.Run = Task.Run(() => RunAsync(session));
        return session.Snapshot();
    }

    /// <summary>Pede o cancelamento e espera o motor encerrar (e restaurar o rádio).</summary>
    public async Task CancelAsync(TimeSpan wait)
    {
        Session? session;
        lock (_lock) session = _session;
        if (session is null || session.Status != TradeSessionStatus.Running)
            return;

        await session.UserCancel.CancelAsync();
        var finished = await Task.WhenAny(session.Run!, Task.Delay(wait, time));
        if (finished != session.Run)
            logger.LogWarning("O motor não encerrou em {Wait}s após o cancelamento.", wait.TotalSeconds);
    }

    /// <summary>
    /// Inscreve um ouvinte. Recebe primeiro os eventos da sessão atual (<paramref name="afterSeq"/>
    /// filtra os já vistos) e depois os novos, de todas as sessões seguintes.
    /// </summary>
    public TradeEventSubscription Subscribe(long afterSeq = 0)
    {
        var channel = Channel.CreateUnbounded<TradeEvent>(new UnboundedChannelOptions { SingleReader = true });
        lock (_lock)
        {
            if (_session is not null)
            {
                foreach (var e in _session.Events.Where(e => e.Seq > afterSeq))
                    channel.Writer.TryWrite(e);
            }
            _subscribers.Add(channel);
        }
        return new TradeEventSubscription(channel.Reader, () =>
        {
            lock (_lock) _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAsync(TimeSpan.FromSeconds(15));
        lock (_lock)
        {
            foreach (var s in _subscribers)
                s.Writer.TryComplete();
            _subscribers.Clear();
        }
    }

    private async Task RunAsync(Session s)
    {
        using var total = new CancellationTokenSource(TimeSpan.FromMinutes(s.Settings.TotalTimeoutMinutes), time);
        TradeFailure? failure = null;
        var status = TradeSessionStatus.Failed;

        Publish(s, new TradeEvent(TradeEventKind.SessionStarted, time.GetUtcNow(),
            $"Troca iniciada ({s.Request.Trades} troca(s), motor {s.Engine.Id})."));

        try
        {
            for (var attempt = 1; attempt <= s.Settings.MaxAttempts; attempt++)
            {
                lock (_lock) s.Attempt = attempt;
                var outcome = await RunAttemptAsync(s, attempt, total.Token);

                if (outcome.Outcome == AttemptOutcome.Completed)
                {
                    status = TradeSessionStatus.Completed;
                    failure = null;
                    break;
                }

                if (s.UserCancel.IsCancellationRequested)
                {
                    status = TradeSessionStatus.Cancelled;
                    failure = new TradeFailure("cancelled", "Troca cancelada.", false);
                    break;
                }

                failure = outcome.Failure ?? new TradeFailure("unknown", "Falha desconhecida.", false);
                Publish(s, new TradeEvent(TradeEventKind.Error, time.GetUtcNow(), failure.Message)
                    { ErrorCode = failure.Code, Attempt = attempt });

                var canRetry = failure.Retryable && !s.ReachedRoom && attempt < s.Settings.MaxAttempts
                    && !total.IsCancellationRequested;
                if (!canRetry)
                    break;

                Publish(s, new TradeEvent(TradeEventKind.RetryScheduled, time.GetUtcNow(),
                    $"Tentando de novo ({attempt + 1} de {s.Settings.MaxAttempts})...")
                    { Attempt = attempt, RetryInSeconds = s.Settings.RetryDelaySeconds });

                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(s.UserCancel.Token, total.Token);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(s.Settings.RetryDelaySeconds), time, waitCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (s.UserCancel.IsCancellationRequested)
                    {
                        status = TradeSessionStatus.Cancelled;
                        failure = new TradeFailure("cancelled", "Troca cancelada.", false);
                    }
                    else
                    {
                        failure = new TradeFailure("total_timeout", "Tempo máximo da troca esgotado.", false);
                    }
                    break;
                }
            }

            if (status == TradeSessionStatus.Failed && s.TradesCompleted > 0)
                status = TradeSessionStatus.PartiallyCompleted;
            if (status == TradeSessionStatus.Cancelled && s.TradesCompleted > 0)
                status = TradeSessionStatus.PartiallyCompleted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro inesperado no coordenador de troca.");
            failure = new TradeFailure("internal_error", "Erro interno do TradeRock.", false);
            status = s.TradesCompleted > 0 ? TradeSessionStatus.PartiallyCompleted : TradeSessionStatus.Failed;
        }

        lock (_lock)
        {
            s.Status = status;
            s.Failure = status == TradeSessionStatus.Completed ? null : failure;
            s.EndedAt = time.GetUtcNow();
        }

        await SaveHistoryAsync(s);

        Publish(s, new TradeEvent(TradeEventKind.SessionFinished, time.GetUtcNow(), s.Failure?.Message)
            { Status = status, ErrorCode = s.Failure?.Code });
    }

    private async Task<TradeAttemptResult> RunAttemptAsync(Session s, int attempt, CancellationToken totalToken)
    {
        using var phase = new CancellationTokenSource(Timeout.InfiniteTimeSpan, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(s.UserCancel.Token, totalToken, phase.Token);

        var lastProgress = TradeEventKind.AttemptStarted;
        phase.CancelAfter(s.Settings.TimeoutAfter(lastProgress));
        Publish(s, new TradeEvent(TradeEventKind.AttemptStarted, time.GetUtcNow(),
            $"Tentativa {attempt} de {s.Settings.MaxAttempts}.") { Attempt = attempt });

        async ValueTask OnEngineEvent(TradeEvent e)
        {
            e = e with { Attempt = attempt };

            if (e.Kind == TradeEventKind.Joined)
            {
                lock (_lock) s.ReachedRoom = true;
            }

            if (e.Kind == TradeEventKind.TradeDone && e.ReceivedData is { } data)
            {
                // Grava no momento da confirmação (como o motor real faz com o .pk3).
                var id = await recorder.StoreReceivedAsync(s.Owner, data, GameCatalog.Of(s.Request.Game).Family,
                    CancellationToken.None);
                lock (_lock)
                {
                    if (e.TradeIndex is { } index && index >= 0 && index < s.Received.Length)
                        s.Received[index] = id;
                    s.TradesCompleted++;
                }
                e = e with { ReceivedPokemonId = id, ReceivedData = null };
            }

            // Log não conta como progresso: o motor real registra "aguardando" a cada 2 s.
            if (e.Kind is not (TradeEventKind.Log or TradeEventKind.Error))
            {
                lastProgress = e.Kind;
                phase.CancelAfter(s.Settings.TimeoutAfter(lastProgress));
            }

            Publish(s, e);
        }

        TradeAttemptResult result;
        try
        {
            result = await s.Engine.RunAttemptAsync(s.Request, OnEngineEvent, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            result = TradeAttemptResult.Cancelled;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "O motor {Engine} falhou.", s.Engine.Id);
            return TradeAttemptResult.Fail("engine_crashed", "O motor de troca parou com um erro.", retryable: true);
        }

        if (result.Outcome == AttemptOutcome.Completed)
            return result;
        if (result.Outcome == AttemptOutcome.Failed && !linked.IsCancellationRequested)
            return result;

        // Cancelado (ou falhou por causa do cancelamento): descobrir por quê.
        if (s.UserCancel.IsCancellationRequested)
            return TradeAttemptResult.Cancelled;
        if (totalToken.IsCancellationRequested)
            return TradeAttemptResult.Fail("total_timeout", "Tempo máximo da troca esgotado.", retryable: false);
        if (lastProgress == TradeEventKind.JoinRequested)
        {
            return TradeAttemptResult.Fail("approval_timeout",
                "O pedido de entrada não foi aprovado no Switch a tempo.", retryable: true);
        }
        if (lastProgress is TradeEventKind.AttemptStarted or TradeEventKind.Scanning
            or TradeEventKind.BeaconFound or TradeEventKind.BeaconDecrypted)
        {
            return TradeAttemptResult.Fail("connect_timeout", "Não foi possível conectar à sala a tempo.", retryable: true);
        }
        return TradeAttemptResult.Fail("phase_timeout", "A troca ficou parada tempo demais.", retryable: true);
    }

    private void Publish(Session s, TradeEvent e)
    {
        lock (_lock)
        {
            e = e with { Seq = ++_lastSeq, SessionId = s.Id };
            s.Events.Add(e);
            foreach (var sub in _subscribers)
                sub.Writer.TryWrite(e);
        }
    }

    private async Task SaveHistoryAsync(Session s)
    {
        try
        {
            TradeRecord record;
            lock (_lock)
            {
                record = new TradeRecord
                {
                    Id = s.Id,
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    EngineId = s.Engine.Id,
                    IsSimulation = s.Engine.IsSimulation,
                    Game = s.Request.Game,
                    TrainerName = s.Request.Trainer.Name,
                    Tid = s.Request.Trainer.Tid,
                    Sid = s.Request.Trainer.Sid,
                    Status = s.Status,
                    Attempts = s.Attempt,
                    TradesPlanned = s.Request.Trades,
                    TradesCompleted = s.TradesCompleted,
                    FailureCode = s.Failure?.Code,
                    FailureMessage = s.Failure?.Message,
                    EventsJson = JsonSerializer.Serialize(s.Events, EventJson),
                    Entries = s.Request.OfferedSlots.Select((slot, i) => new TradeRecordEntry
                    {
                        TradeIndex = i,
                        SentPokemonId = s.Request.Party[slot].PokemonId,
                        ReceivedPokemonId = s.Received[i],
                    }).ToList(),
                };
            }
            await recorder.SaveAsync(s.Owner, record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Não foi possível gravar o histórico da troca {Id}.", s.Id);
        }
    }

    private sealed class Session(Guid id, Guid owner, ITradeEngine engine, TradeRequest request, TradeSettings settings, DateTimeOffset startedAt)
    {
        public Guid Id { get; } = id;
        public Guid Owner { get; } = owner;
        public ITradeEngine Engine { get; } = engine;
        public TradeRequest Request { get; } = request;
        public TradeSettings Settings { get; } = settings;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public CancellationTokenSource UserCancel { get; } = new();
        public Task? Run { get; set; }

        // Protegidos pelo _lock do coordenador.
        public TradeSessionStatus Status { get; set; } = TradeSessionStatus.Running;
        public int Attempt { get; set; }
        public bool ReachedRoom { get; set; }
        public int TradesCompleted { get; set; }
        public Guid?[] Received { get; } = new Guid?[request.Trades];
        public TradeFailure? Failure { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public List<TradeEvent> Events { get; } = [];

        public TradeSessionSnapshot Snapshot() => new(
            Id, Owner, Engine.Id, Engine.IsSimulation, Status, Attempt, Settings.MaxAttempts, Request.Trades,
            TradesCompleted, StartedAt, EndedAt, Request.Trainer.Name, Request.Game,
            Failure?.Code, Failure?.Message, Received.ToArray(), Request.LinkCode);
    }
}

public sealed class TradeEventSubscription(ChannelReader<TradeEvent> reader, Action dispose) : IDisposable
{
    public ChannelReader<TradeEvent> Reader { get; } = reader;
    public void Dispose() => dispose();
}
