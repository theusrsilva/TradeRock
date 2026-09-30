using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace TradeRock.App.Server;

/// <summary>
/// Senhas das contas do modo servidor: Argon2id com sal aleatório, guardado no formato PHC
/// ($argon2id$v=19$m=19456,t=2,p=1$sal$hash, base64 sem "="). Parâmetros mínimos recomendados pela OWASP.
/// </summary>
public static class PasswordHasher
{
    public const int MemoryKiB = 19456;
    public const int Iterations = 2;
    public const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, MemoryKiB, Iterations, Parallelism, HashBytes);
        return $"$argon2id$v=19$m={MemoryKiB},t={Iterations},p={Parallelism}${B64(salt)}${B64(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        // ["", "argon2id", "v=19", "m=..,t=..,p=..", sal, hash]
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
            return false;
        var cost = parts[3].Split(',').Select(p => p.Split('=')).Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0], kv => int.TryParse(kv[1], out var n) ? n : -1);
        if (!cost.TryGetValue("m", out var m) || !cost.TryGetValue("t", out var t) || !cost.TryGetValue("p", out var p)
            || m is < 8 or > 1 << 20 || t is < 1 or > 16 || p is < 1 or > 16)
            return false;
        byte[] salt, expected;
        try
        {
            salt = FromB64(parts[4]);
            expected = FromB64(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (expected.Length is < 16 or > 64)
            return false;
        var actual = Derive(password, salt, m, t, p, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Parâmetros antigos (mudança futura dos custos): refazer o hash no próximo login.</summary>
    public static bool NeedsRehash(string encoded) =>
        !encoded.StartsWith($"$argon2id$v=19$m={MemoryKiB},t={Iterations},p={Parallelism}$", StringComparison.Ordinal);

    private static byte[] Derive(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }

    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=');

    private static byte[] FromB64(string text) => Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '='));
}
