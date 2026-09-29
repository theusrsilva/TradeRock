using System.Text.Json.Serialization;

namespace TradeRock.Core.Trading;

/// <summary>
/// Eventos de uma troca. Os de motor correspondem aos marcos que o frlg-ldn-trade registra
/// com lg.info/self.info (ex.: "Joining the host..." → JoinRequested); os de sessão são do TradeRock.
/// </summary>
public enum TradeEventKind
{
    // Sessão (coordenador)
    SessionStarted,
    AttemptStarted,
    RetryScheduled,
    SessionFinished,

    // Motor
    Scanning,
    BeaconFound,
    BeaconDecrypted,
    JoinRequested,
    Joined,
    InRoom,
    HostSat,
    CardsExchanged,
    TradeMenuOpen,
    TradeOffered,
    Confirming,
    TradeDone,
    TradeCancelled,
    Leaving,
    LinkClosed,
    Error,
    Log,
}

public enum TradeSessionStatus
{
    Running,
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled,
}

public sealed record TradeEvent(TradeEventKind Kind, DateTimeOffset At, string? Message = null)
{
    /// <summary>Número sequencial crescente (global no processo; usado pelo SSE para retomar).</summary>
    public long Seq { get; init; }

    public Guid SessionId { get; init; }

    public int? Attempt { get; init; }

    /// <summary>Índice da troca (0-based) para TradeOffered/Confirming/TradeDone.</summary>
    public int? TradeIndex { get; init; }

    /// <summary>Preenchido pelo coordenador depois de guardar o recebido no cofre.</summary>
    public Guid? ReceivedPokemonId { get; init; }

    public string? ErrorCode { get; init; }

    public TradeSessionStatus? Status { get; init; }

    /// <summary>.pk3 recebido (só em TradeDone vindo do motor). Nunca serializado.</summary>
    [JsonIgnore]
    public byte[]? ReceivedData { get; init; }

    /// <summary>Atraso até a próxima tentativa (RetryScheduled).</summary>
    public double? RetryInSeconds { get; init; }
}
