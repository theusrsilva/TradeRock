using System.ComponentModel;
using System.Diagnostics;
using TradeRock.Core.Engines.Worker;

namespace TradeRock.Core.Updates;

/// <summary>Escolhe o instalador pelo jeito que o app foi instalado (D29).</summary>
public static class UpdateInstallers
{
    /// <summary>Pasta do .deb (scripts/package-linux.sh).</summary>
    public const string DebAppDir = "/usr/lib/traderock/app/";

    public static InstallKind Detect(string baseDirectory, Func<string, string?> env)
    {
        if (OperatingSystem.IsWindows())
        {
            // O instalador põe o app em Arquivos de Programas, com o desinstalador do Inno Setup ao lado.
            var root = Path.GetFullPath(baseDirectory);
            return File.Exists(Path.Combine(root, "unins000.exe")) ? InstallKind.WindowsInstaller : InstallKind.None;
        }
        if (OperatingSystem.IsLinux())
        {
            if (env("APPIMAGE") is { Length: > 0 } image && File.Exists(image))
                return InstallKind.AppImage;
            if (Path.GetFullPath(baseDirectory).StartsWith(DebAppDir, StringComparison.Ordinal))
                return InstallKind.Deb;
        }
        return InstallKind.None;
    }

    public static IUpdateInstaller Create(InstallKind kind, IPrivilegedShell shell) => kind switch
    {
        InstallKind.WindowsInstaller => new WindowsInstaller(),
        InstallKind.AppImage when !OperatingSystem.IsWindows() => new AppImageInstaller(Environment.GetEnvironmentVariable("APPIMAGE")!),
        InstallKind.Deb => new DebInstaller(shell),
        _ => new NoInstaller(),
    };

    /// <summary>Abre o app novo depois que este fechar (o banco e a pasta de dados são os mesmos).</summary>
    internal static void RelaunchLater(string program)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var arg in new[] { "-c", "sleep 3; exec \"$0\" >/dev/null 2>&1", program })
            start.ArgumentList.Add(arg);
        Process.Start(start);
    }
}

internal sealed class NoInstaller : IUpdateInstaller
{
    public InstallKind Kind => InstallKind.None;

    public Task InstallAsync(string file, string sha256, CancellationToken ct) =>
        throw new UpdateException("update_manual", "Esta instalação não se atualiza sozinha.");
}

/// <summary>
/// Windows: roda o instalador do Release em modo silencioso (o Windows pede o administrador, como na instalação) e o
/// app fecha. O instalador reabre o app como o usuário (/TRADEROCKRESTART=1, packaging/windows/traderock.iss).
/// </summary>
internal sealed class WindowsInstaller : IUpdateInstaller
{
    public InstallKind Kind => InstallKind.WindowsInstaller;

    public Task InstallAsync(string file, string sha256, CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /TRADEROCKRESTART=1")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new UpdateException("update_cancelled", "A permissão de administrador foi recusada.");
        }
        return Task.CompletedTask;
    }
}

/// <summary>AppImage: troca o arquivo no lugar (rename na mesma pasta) e reabre. Sem root.</summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
internal sealed class AppImageInstaller(string target) : IUpdateInstaller
{
    public InstallKind Kind => InstallKind.AppImage;

    public Task InstallAsync(string file, string sha256, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.new");
        try
        {
            File.Copy(file, temp, overwrite: true);
            File.SetUnixFileMode(temp, File.GetUnixFileMode(target));
            // O app aberto continua com o arquivo antigo (a montagem usa o inode dele).
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw new UpdateException("update_no_permission", $"Sem permissão para trocar {target}: {e.Message}");
        }
        UpdateInstallers.RelaunchLater(target);
        return Task.CompletedTask;
    }
}

/// <summary>
/// .deb: o dpkg roda como root pelo pkexec (a senha é pedida pelo sistema). O pacote vai pela entrada padrão para um
/// arquivo do root e é conferido de novo, como root, antes de instalar: o arquivo do usuário nunca é usado pelo root.
/// </summary>
internal sealed class DebInstaller(IPrivilegedShell shell) : IUpdateInstaller
{
    public const string Script =
        "t=$(mktemp /tmp/traderock-update.XXXXXX) || exit 1; cat > \"$t\"; " +
        "if echo \"$1  $t\" | sha256sum -c --quiet; then dpkg -i \"$t\"; r=$?; else r=3; fi; rm -f \"$t\"; exit $r";

    public InstallKind Kind => InstallKind.Deb;

    public async Task InstallAsync(string file, string sha256, CancellationToken ct)
    {
        var (code, error) = await shell.RunAsync(Script, [sha256], async stdin =>
        {
            await using var source = File.OpenRead(file);
            await source.CopyToAsync(stdin, ct);
        }, ct);
        if (code is 126 or 127)
            throw new UpdateException("update_cancelled", "A senha do sistema não foi confirmada.");
        if (code != 0)
            throw new UpdateException("update_failed", $"dpkg terminou com o código {code}: {error}");
        UpdateInstallers.RelaunchLater("/usr/bin/traderock");
    }
}
