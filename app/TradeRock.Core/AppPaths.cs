namespace TradeRock.Core;

/// <summary>
/// Pastas do usuário onde o TradeRock guarda banco, cofre, chaves e logs.
/// Linux: $XDG_DATA_HOME/traderock (padrão ~/.local/share/traderock).
/// Windows: %APPDATA%\TradeRock.
/// </summary>
public sealed class AppPaths
{
    public string DataDir { get; }
    public string LogsDir => Path.Combine(DataDir, "logs");
    public string VaultDir => Path.Combine(DataDir, "vault");
    public string KeysDir => Path.Combine(DataDir, "keys");
    public string DatabasePath => Path.Combine(DataDir, "traderock.db");

    public AppPaths(string dataDir)
    {
        DataDir = Path.GetFullPath(dataDir);
    }

    /// <summary>Resolve a pasta padrão. <paramref name="overrideDir"/> vence (usado em testes e no modo dev).</summary>
    public static AppPaths ForCurrentUser(string? overrideDir = null, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;

        if (!string.IsNullOrWhiteSpace(overrideDir))
            return new AppPaths(overrideDir);

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new AppPaths(Path.Combine(appData, "TradeRock"));
        }

        var xdg = env("XDG_DATA_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return new AppPaths(Path.Combine(baseDir, "traderock"));
    }

    /// <summary>Cria as pastas. No Linux, a pasta de dados fica 700 e a de chaves também.</summary>
    public void EnsureCreated()
    {
        foreach (var dir in new[] { DataDir, LogsDir, VaultDir, KeysDir })
        {
            Directory.CreateDirectory(dir);
        }

        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            File.SetUnixFileMode(DataDir, ownerOnly);
            File.SetUnixFileMode(KeysDir, ownerOnly);
        }
    }
}
