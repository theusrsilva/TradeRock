using Microsoft.Extensions.Logging;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Activities;

public enum ActivityStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record ActivitySnapshot(
    Guid Id,
    TradeGame Game,
    ActivityKind Kind,
    string? GiftLabel,
    ActivityStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ActivityEvent> Events,
    string? ErrorCode,
    string? ErrorMessage,
    string? RaidKind = null);

/// <summary>Já há uma troca ou outra atividade usando o rádio.</summary>
public sealed class RadioBusyException() : Exception("O rádio está ocupado.");

/// <summary>
/// Uma atividade por vez, sem fila (D26): a pessoa acompanha pela tela e cancela por ela. O rádio é o mesmo da
/// troca, então uma não começa enquanto a outra roda.
/// </summary>
public sealed class ActivityService(TradeCoordinator trades, TimeProvider time, ILogger<ActivityService> logger)
{
    private readonly Lock _lock = new();
    private Session? _session;

    public bool IsBusy
    {
        get { lock (_lock) return _session is { Status: ActivityStatus.Running }; }
    }

    /// <summary>A atividade atual ou a última (null se nenhuma nesta execução).</summary>
    public ActivitySnapshot? Current
    {
        get { lock (_lock) return _session?.Snapshot(); }
    }

    public ActivitySnapshot Start(IActivityEngine engine, ActivityRequest request, string? giftLabel)
    {
        Session session;
        lock (_lock)
        {
            if (_session is { Status: ActivityStatus.Running } || trades.IsBusy)
                throw new RadioBusyException();
            session = new Session(Guid.NewGuid(), request, giftLabel, time.GetUtcNow());
            _session = session;
        }
        _ = Task.Run(() => RunAsync(engine, session));
        return session.Snapshot();
    }

    public bool Cancel()
    {
        lock (_lock)
        {
            if (_session is not { Status: ActivityStatus.Running } s)
                return false;
            s.Cancel.Cancel();
            return true;
        }
    }

    private async Task RunAsync(IActivityEngine engine, Session s)
    {
        ActivityResult result;
        try
        {
            result = await engine.RunActivityAsync(s.Request, e =>
            {
                lock (_lock)
                    s.Events.Add(e);
                return ValueTask.CompletedTask;
            }, s.Cancel.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Atividade {Id} parou com erro.", s.Id);
            result = ActivityResult.Fail("engine_error", e.Message);
        }
        catch (OperationCanceledException)
        {
            result = ActivityResult.Cancelled;
        }
        lock (_lock)
        {
            s.Status = result.Ok ? ActivityStatus.Completed : result.Code == "cancelled" ? ActivityStatus.Cancelled : ActivityStatus.Failed;
            s.ErrorCode = result.Ok || s.Status == ActivityStatus.Cancelled ? null : result.Code;
            s.ErrorMessage = s.ErrorCode is null ? null : result.Message;
            s.FinishedAt = time.GetUtcNow();
            s.Events.Add(new ActivityEvent(ActivityEventKind.Finished, s.FinishedAt.Value));
        }
        s.Cancel.Dispose();
    }

    private sealed class Session(Guid id, ActivityRequest request, string? giftLabel, DateTimeOffset startedAt)
    {
        public Guid Id { get; } = id;
        public ActivityRequest Request { get; } = request;
        public CancellationTokenSource Cancel { get; } = new();
        public List<ActivityEvent> Events { get; } = [];
        public ActivityStatus Status { get; set; } = ActivityStatus.Running;
        public DateTimeOffset? FinishedAt { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public ActivitySnapshot Snapshot() => new(Id, Request.Game, Request.Kind, giftLabel, Status, startedAt, FinishedAt,
            [.. Events], ErrorCode, ErrorMessage, Request.Raid?.Kind ?? Request.Circle?.Kind);
    }
}
