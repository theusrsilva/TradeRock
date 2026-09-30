using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace TradeRock.App.Server;

public sealed record LanSession(string Id, Guid UserId, string Csrf);

/// <summary>
/// Logins feitos pela rede. Ficam só na memória: fechar o app ou desligar o modo servidor derruba todos.
/// Expiram depois de 12 horas sem uso.
/// </summary>
public sealed class LanSessions(TimeProvider time)
{
    public static readonly TimeSpan Idle = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<string, (LanSession Session, DateTimeOffset LastSeen)> _sessions = new(StringComparer.Ordinal);

    public LanSession Create(Guid userId)
    {
        var session = new LanSession(NewToken(), userId, NewToken());
        _sessions[session.Id] = (session, time.GetUtcNow());
        return session;
    }

    public LanSession? Get(string? id)
    {
        if (string.IsNullOrEmpty(id) || !_sessions.TryGetValue(id, out var entry))
            return null;
        var now = time.GetUtcNow();
        if (now - entry.LastSeen > Idle)
        {
            _sessions.TryRemove(id, out _);
            return null;
        }
        _sessions[id] = (entry.Session, now);
        return entry.Session;
    }

    public void Remove(string id) => _sessions.TryRemove(id, out _);

    /// <summary>Derruba os logins de uma conta (senha trocada, conta desativada ou apagada).</summary>
    public void RemoveUser(Guid userId)
    {
        foreach (var (id, entry) in _sessions)
            if (entry.Session.UserId == userId)
                _sessions.TryRemove(id, out _);
    }

    public void Clear() => _sessions.Clear();

    public int Count => _sessions.Count;

    private static string NewToken() => Base64UrlToken(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlToken(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Freio contra adivinhar senhas: 10 erros por endereço em 15 minutos bloqueiam o endereço até a janela
/// passar, e 5 erros seguidos numa conta a travam por 5 minutos (qualquer endereço).
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    public const int MaxPerAddress = 10;
    public static readonly TimeSpan AddressWindow = TimeSpan.FromMinutes(15);
    public const int MaxPerAccount = 5;
    public static readonly TimeSpan AccountLock = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly Dictionary<IPAddress, List<DateTimeOffset>> _byAddress = [];
    private readonly Dictionary<string, (int Failures, DateTimeOffset? LockedUntil)> _byAccount = new(StringComparer.Ordinal);

    /// <summary>Segundos até poder tentar de novo; 0 = pode tentar.</summary>
    public int RetryAfter(IPAddress address, string username)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            var wait = TimeSpan.Zero;
            if (_byAddress.TryGetValue(address, out var failures))
            {
                failures.RemoveAll(f => now - f > AddressWindow);
                if (failures.Count >= MaxPerAddress)
                    wait = failures[0] + AddressWindow - now;
            }
            if (_byAccount.TryGetValue(username, out var account) && account.LockedUntil is { } until && until > now)
                wait = until - now > wait ? until - now : wait;
            return wait <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(wait.TotalSeconds);
        }
    }

    public void Failed(IPAddress address, string username)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            if (!_byAddress.TryGetValue(address, out var failures))
                _byAddress[address] = failures = [];
            failures.Add(now);

            var (count, _) = _byAccount.GetValueOrDefault(username);
            count++;
            _byAccount[username] = count >= MaxPerAccount ? (0, now + AccountLock) : (count, null);
        }
    }

    public void Succeeded(string username)
    {
        lock (_lock) _byAccount.Remove(username);
    }
}
