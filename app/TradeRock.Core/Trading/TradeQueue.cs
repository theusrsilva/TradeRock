using Microsoft.Extensions.Logging;

namespace TradeRock.Core.Trading;

public sealed class AlreadyQueuedException() : InvalidOperationException("Esta conta já tem uma troca na fila ou em andamento.");

/// <summary>Posição na fila: 0 = trocando agora, 1 = a próxima, e assim por diante.</summary>
public sealed record QueueEntryInfo(Guid Id, Guid OwnerId, string? OwnerName, DateTimeOffset EnqueuedAt, int Position, Guid? SessionId);

/// <summary>Resultado de entrar na fila: começou na hora (<see cref="Started"/>) ou está esperando.</summary>
public sealed record QueueTicket(Guid EntryId, int Position, TradeSessionSnapshot? Started);

/// <summary>
/// Um rádio, uma troca por vez. Os pedidos esperam aqui na ordem de chegada e começam sozinhos quando o
/// rádio fica livre. Cada conta tem no máximo um pedido (na fila ou trocando).
/// </summary>
public sealed class TradeQueue(TradeCoordinator coordinator, TimeProvider time, ILogger<TradeQueue> logger)
{
    private readonly Lock _lock = new();
    private readonly List<Entry> _waiting = [];
    private Entry? _running;

    public QueueTicket Enqueue(Guid owner, string? ownerName, ITradeEngine engine, TradeRequest request, TradeSettings settings)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            throw new InvalidTradeRequestException(errors);

        var entry = new Entry(Guid.CreateVersion7(), owner, ownerName, engine, request, settings, time.GetUtcNow());
        lock (_lock)
        {
            if (_waiting.Any(e => e.Owner == owner) || (_running?.Owner == owner && coordinator.IsBusy))
                throw new AlreadyQueuedException();
            _waiting.Add(entry);
            Pump();
            return entry.Session is { } started
                ? new QueueTicket(entry.Id, 0, started)
                : new QueueTicket(entry.Id, _waiting.IndexOf(entry) + 1, null);
        }
    }

    /// <summary>Sai da fila (quem pediu ou um administrador, <paramref name="owner"/> null). A troca em andamento se cancela à parte.</summary>
    public bool Leave(Guid entryId, Guid? owner)
    {
        lock (_lock)
            return _waiting.RemoveAll(e => e.Id == entryId && (owner is null || e.Owner == owner)) > 0;
    }

    /// <summary>Quem está trocando (posição 0, se houver) e quem espera, em ordem.</summary>
    public IReadOnlyList<QueueEntryInfo> Snapshot()
    {
        lock (_lock)
        {
            var list = new List<QueueEntryInfo>();
            if (_running is { } r && coordinator.IsBusy)
                list.Add(r.Info(0));
            list.AddRange(_waiting.Select((e, i) => e.Info(i + 1)));
            return list;
        }
    }

    public int WaitingCount
    {
        get { lock (_lock) return _waiting.Count; }
    }

    /// <summary>Com o rádio livre, começa o próximo. Chamado com o _lock.</summary>
    private void Pump()
    {
        while (_waiting.Count > 0 && !coordinator.IsBusy)
        {
            var next = _waiting[0];
            _waiting.RemoveAt(0);
            try
            {
                next.Session = coordinator.Start(next.Engine, next.Request, next.Settings, next.Owner);
            }
            catch (TradeBusyException)
            {
                // Alguém começou por fora da fila: espera esta terminar.
                _waiting.Insert(0, next);
                _ = coordinator.Completion.ContinueWith(_ => Advance(), TaskScheduler.Default);
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Não foi possível começar a troca da fila.");
                continue;
            }
            _running = next;
            _ = coordinator.Completion.ContinueWith(_ => Advance(), TaskScheduler.Default);
            return;
        }
    }

    private void Advance()
    {
        lock (_lock) Pump();
    }

    private sealed class Entry(Guid id, Guid owner, string? ownerName, ITradeEngine engine, TradeRequest request,
        TradeSettings settings, DateTimeOffset enqueuedAt)
    {
        public Guid Id { get; } = id;
        public Guid Owner { get; } = owner;
        public ITradeEngine Engine { get; } = engine;
        public TradeRequest Request { get; } = request;
        public TradeSettings Settings { get; } = settings;
        public TradeSessionSnapshot? Session { get; set; }

        public QueueEntryInfo Info(int position) => new(Id, Owner, ownerName, enqueuedAt, position, Session?.Id);
    }
}
