namespace TradeRock.Core.Setup;

public sealed record KeysValidation(
    bool Valid,
    IReadOnlyList<int> InvalidLines,
    IReadOnlyList<string> PresentRequired,
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<int> Protocols, // versões do LDN suportadas: 1 (master_key_00) e/ou 3 (master_key_12)
    int KeyCount);

public sealed class ProdKeysException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// prod.keys: validação do formato e guarda com permissão 600. Os VALORES nunca saem daqui:
/// não são devolvidos, logados, exportados nem incluídos em relatórios. A validação só prova que o
/// arquivo é legível e tem as chaves certas; a prova de que os valores estão corretos é o teste com
/// o Switch (o beacon da sala precisa ser decifrado).
/// </summary>
public sealed class ProdKeys(AppPaths paths)
{
    public const int MaxBytes = 64 * 1024;

    /// <summary>
    /// Chaves usadas pela biblioteca LDN (kinnay/LDN, KeyDerivation): as duas "generation source"
    /// e uma master key por versão de protocolo. O KeyFile.cs do easyworld exige o mesmo.
    /// </summary>
    public static readonly string[] AlwaysRequired = ["aes_kek_generation_source", "aes_key_generation_source"];
    public static readonly string[] MasterKeys = ["master_key_00", "master_key_12"];

    public string FilePath => Path.Combine(paths.KeysDir, "prod.keys");

    /// <summary>
    /// Mesmo formato que o ldn.load_keys: cada linha não vazia é "nome = hex". Linhas fora desse
    /// formato fazem o loader do motor falhar, então tornam o arquivo inválido.
    /// </summary>
    public static KeysValidation Validate(ReadOnlySpan<byte> file)
    {
        string text;
        try
        {
            text = new System.Text.UTF8Encoding(false, true).GetString(file);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return new KeysValidation(false, [0], [], [.. AlwaysRequired, MasterKeys[0]], [], 0);
        }

        var keys = new Dictionary<string, int>(StringComparer.Ordinal); // nome → tamanho em bytes
        var invalid = new List<int>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;
            var parts = line.Split('=');
            if (parts.Length != 2 || parts[0].Trim().Length == 0 || !TryHexLength(parts[1], out var length))
            {
                invalid.Add(i + 1);
                continue;
            }
            keys[parts[0].Trim()] = length;
        }

        bool Has(string name) => keys.TryGetValue(name, out var len) && len == 16; // chaves AES-128
        var present = AlwaysRequired.Concat(MasterKeys).Where(Has).ToList();
        var protocols = new List<int>();
        if (Has("master_key_00")) protocols.Add(1);
        if (Has("master_key_12")) protocols.Add(3);

        var missing = AlwaysRequired.Where(k => !Has(k)).ToList();
        if (protocols.Count == 0)
            missing.Add("master_key_00");

        return new KeysValidation(invalid.Count == 0 && missing.Count == 0, invalid, present, missing, protocols, keys.Count);
    }

    /// <summary>bytes.fromhex do Python: pares hex, espaços em branco ignorados.</summary>
    private static bool TryHexLength(string value, out int length)
    {
        var hex = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        length = hex.Length / 2;
        return hex.Length > 0 && hex.Length % 2 == 0 && hex.All(Uri.IsHexDigit);
    }

    /// <summary>Guarda (só se válido) com permissão 600 no Linux. Devolve o resultado da validação.</summary>
    public KeysValidation Save(ReadOnlySpan<byte> file)
    {
        if (file.Length is 0 or > MaxBytes)
            throw new ProdKeysException("keys_size");
        var validation = Validate(file);
        if (!validation.Valid)
            return validation;

        paths.EnsureCreated();
        var temp = FilePath + ".tmp";
        if (!OperatingSystem.IsWindows())
        {
            // Cria já com 600, antes de escrever qualquer byte.
            using var stream = new FileStream(temp, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
            stream.Write(file);
        }
        else
        {
            // Windows (Fase 2): a pasta fica no perfil do usuário; ACL só do usuário entra junto com o instalador.
            File.WriteAllBytes(temp, file.ToArray());
        }
        File.Move(temp, FilePath, overwrite: true);
        return validation;
    }

    /// <summary>Estado atual do arquivo guardado (sem valores).</summary>
    public KeysValidation? Current()
    {
        if (!File.Exists(FilePath))
            return null;
        return Validate(File.ReadAllBytes(FilePath));
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
