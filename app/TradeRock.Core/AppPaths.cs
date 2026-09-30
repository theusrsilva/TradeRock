using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TradeRock.Core;

/// <summary>
/// Pastas do usuário onde o TradeRock guarda banco, cofre, chaves e logs.
/// Linux: $XDG_DATA_HOME/traderock (padrão ~/.local/share/traderock).
/// Windows: %LOCALAPPDATA%\TradeRock (não o %APPDATA%, que acompanha o perfil móvel: o prod.keys não viaja).
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
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new AppPaths(Path.Combine(localAppData, "TradeRock"));
        }

        var xdg = env("XDG_DATA_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return new AppPaths(Path.Combine(baseDir, "traderock"));
    }

    /// <summary>
    /// Cria as pastas. A pasta de dados e a de chaves ficam só do usuário: 700 no Linux e, no Windows, uma
    /// ACL sem herança só com o usuário atual (o que for criado dentro herda a mesma regra).
    /// </summary>
    public void EnsureCreated()
    {
        foreach (var dir in new[] { DataDir, LogsDir, VaultDir, KeysDir })
        {
            Directory.CreateDirectory(dir);
        }

        if (OperatingSystem.IsWindows())
        {
            RestrictToCurrentUser(DataDir);
            RestrictToCurrentUser(KeysDir);
        }
        else
        {
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            File.SetUnixFileMode(DataDir, ownerOnly);
            File.SetUnixFileMode(KeysDir, ownerOnly);
        }
    }

    /// <summary>Windows: só o usuário atual (controle total), sem herdar as permissões da pasta de cima.</summary>
    [SupportedOSPlatform("windows")]
    public static void RestrictToCurrentUser(string directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Usuário do Windows sem SID.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
    }
}
