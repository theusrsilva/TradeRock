using System.Security.Cryptography;
using System.Text;

namespace TradeRock.Core;

/// <summary>
/// Segredo aleatório gerado a cada execução. Só a janela do app recebe o valor
/// (na URL inicial); qualquer outro processo da máquina fica sem acesso à API.
/// </summary>
public sealed class SessionToken
{
    public string Value { get; }

    private readonly byte[] _bytes;

    public SessionToken(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 32)
            throw new ArgumentException("O token de sessão precisa ter pelo menos 32 caracteres.", nameof(value));
        Value = value;
        _bytes = Encoding.UTF8.GetBytes(value);
    }

    public static SessionToken Generate()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        var text = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new SessionToken(text);
    }

    /// <summary>Comparação em tempo constante.</summary>
    public bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return false;
        return CryptographicOperations.FixedTimeEquals(_bytes, Encoding.UTF8.GetBytes(candidate));
    }

    public override string ToString() => "SessionToken(***)";
}
