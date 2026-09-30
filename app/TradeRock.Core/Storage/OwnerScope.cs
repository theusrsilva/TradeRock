namespace TradeRock.Core.Storage;

/// <summary>Registro que pertence ao cofre de uma conta (modo servidor: um cofre por usuário).</summary>
public interface IOwned
{
    Guid OwnerId { get; set; }
}

/// <summary>
/// De quem é o cofre da requisição atual. A janela local é sempre o dono do PC (<see cref="LocalOwner"/>,
/// que também é o dono dos dados de antes do modo servidor). Cada contexto do EF Core lê o dono ao ser
/// criado e filtra as consultas por ele; registros novos recebem esse dono ao serem gravados.
/// </summary>
public static class OwnerScope
{
    public static readonly Guid LocalOwner = new("00000000-0000-0000-0000-000000000001");

    private static readonly AsyncLocal<Guid?> Scope = new();

    public static Guid Current => Scope.Value ?? LocalOwner;

    /// <summary>Troca o dono até o Dispose (vale para o fluxo assíncrono atual).</summary>
    public static IDisposable Use(Guid owner)
    {
        var previous = Scope.Value;
        Scope.Value = owner;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Scope.Value = previous;
    }
}
