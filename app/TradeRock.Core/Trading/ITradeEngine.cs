using TradeRock.Core.Games;

namespace TradeRock.Core.Trading;

public enum AttemptOutcome
{
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// Falha de uma tentativa. <see cref="Code"/> é estável (traduzido no frontend).
/// <see cref="Retryable"/> diz se o motor considera seguro tentar de novo; o coordenador ainda
/// recusa repetir depois que a sala de troca foi alcançada.
/// </summary>
public sealed record TradeFailure(string Code, string Message, bool Retryable);

public sealed record TradeAttemptResult(AttemptOutcome Outcome, TradeFailure? Failure = null)
{
    public static TradeAttemptResult Completed { get; } = new(AttemptOutcome.Completed);
    public static TradeAttemptResult Cancelled { get; } = new(AttemptOutcome.Cancelled);
    public static TradeAttemptResult Fail(string code, string message, bool retryable) =>
        new(AttemptOutcome.Failed, new TradeFailure(code, message, retryable));
}

public sealed record EngineAvailability(bool Available, string? ReasonCode = null);

/// <summary>
/// Motor de troca plugável. Uma chamada a <see cref="RunAttemptAsync"/> é UMA tentativa de conexão
/// e troca; retry, timeouts e histórico ficam no <see cref="TradeCoordinator"/>.
/// Cada Pokémon recebido chega num evento <see cref="TradeEventKind.TradeDone"/> com
/// <see cref="TradeEvent.ReceivedData"/>, no momento em que a troca é confirmada (como o motor
/// real faz: grava na hora, para sobreviver a um encerramento abrupto).
/// Ao ser cancelado, o motor encerra e restaura o estado do rádio antes de retornar.
/// </summary>
public interface ITradeEngine
{
    /// <summary>"fake", "python-ldn", "esp32".</summary>
    string Id { get; }

    /// <summary>Simulação (modo demonstração): nada é trocado de verdade.</summary>
    bool IsSimulation { get; }

    Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    /// <summary>Jogos que o motor sabe trocar. Os motores reais do FRLG só trocam com o FireRed/LeafGreen.</summary>
    bool Supports(GameFamily family) => family == GameFamily.Frlg;

    Task<TradeAttemptResult> RunAttemptAsync(
        TradeRequest request,
        Func<TradeEvent, ValueTask> emit,
        CancellationToken cancellationToken);
}
