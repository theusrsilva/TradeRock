namespace TradeRock.Core.Trading;

/// <summary>Configuração do assistente de troca (editável em Configurações).</summary>
public sealed record TradeSettings
{
    /// <summary>Tentativas de conexão (retry automático). A especificação pede 5 por padrão.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Espera entre tentativas, para o rádio assentar (o motor real também espera).</summary>
    public double RetryDelaySeconds { get; init; } = 3;

    /// <summary>Procurar e decifrar a sala, antes do pedido de entrada.</summary>
    public double ConnectTimeoutSeconds { get; init; } = 90;

    /// <summary>Esperando o jogador aprovar o pedido de entrada no Switch.</summary>
    public double ApprovalTimeoutSeconds { get; init; } = 120;

    /// <summary>Sem progresso dentro da sala (andar, escolher, confirmar: ritmo humano).</summary>
    public double PhaseTimeoutSeconds { get; init; } = 300;

    /// <summary>Limite da sessão inteira.</summary>
    public double TotalTimeoutMinutes { get; init; } = 30;

    public static TradeSettings Default { get; } = new();

    /// <summary>Mantém valores dentro de limites seguros.</summary>
    public TradeSettings Normalized() => this with
    {
        MaxAttempts = Math.Clamp(MaxAttempts, 1, 20),
        RetryDelaySeconds = Math.Clamp(RetryDelaySeconds, 0, 60),
        ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, 1, 600),
        ApprovalTimeoutSeconds = Math.Clamp(ApprovalTimeoutSeconds, 1, 900),
        PhaseTimeoutSeconds = Math.Clamp(PhaseTimeoutSeconds, 1, 1800),
        TotalTimeoutMinutes = Math.Clamp(TotalTimeoutMinutes, 0.01, 180),
    };

    public TimeSpan TimeoutAfter(TradeEventKind lastProgress) => TimeSpan.FromSeconds(lastProgress switch
    {
        TradeEventKind.SessionStarted or TradeEventKind.AttemptStarted or TradeEventKind.Scanning
            or TradeEventKind.BeaconFound or TradeEventKind.BeaconDecrypted => ConnectTimeoutSeconds,
        TradeEventKind.JoinRequested => ApprovalTimeoutSeconds,
        _ => PhaseTimeoutSeconds,
    });
}
