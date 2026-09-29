using System.Diagnostics;
using System.Formats.Tar;

namespace TradeRock.Core.Engines.Worker;

/// <summary>Worker embutido no AppImage: há um para instalar? Já está instalado? É o desta versão?</summary>
public sealed record BundledWorkerStatus(bool Available, bool Installed, bool UpToDate)
{
    public static readonly BundledWorkerStatus None = new(false, false, false);
}

/// <summary>Roda o script de instalação como root. O padrão é o pkexec; os testes rodam sem root.</summary>
public interface IPrivilegedShell
{
    /// <summary>
    /// Roda <c>/bin/sh -c script</c> com os argumentos dados. <paramref name="stdin"/> escreve a entrada
    /// padrão (o .tar do worker). Devolve o código de saída e o fim da saída de erro.
    /// </summary>
    Task<(int ExitCode, string Error)> RunAsync(string script, IReadOnlyList<string> args, Func<Stream, Task>? stdin, CancellationToken ct);
}

/// <summary>
/// pkexec /bin/sh -c "script" ...: o script vem da linha de comando do app, não de um arquivo, e o
/// worker vem pela entrada padrão. Assim nenhum arquivo gravável pelo usuário é executado como root, e
/// o root não precisa ler a montagem FUSE do AppImage (que é só do usuário).
/// </summary>
public sealed class PkexecShell : IPrivilegedShell
{
    public async Task<(int ExitCode, string Error)> RunAsync(string script, IReadOnlyList<string> args, Func<Stream, Task>? stdin, CancellationToken ct)
    {
        var pkexec = WorkerLocator.PkexecPath() ?? throw new WorkerException("pkexec_missing", "pkexec não encontrado.");
        return await ShellRunner.RunAsync(pkexec, ["/bin/sh", "-c", script, "traderock-worker-setup", .. args], stdin, ct);
    }
}

public static class ShellRunner
{
    public static async Task<(int ExitCode, string Error)> RunAsync(string program, IEnumerable<string> args, Func<Stream, Task>? stdin, CancellationToken ct)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new WorkerException("worker_install_failed", $"Não foi possível iniciar {program}.");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        try
        {
            if (stdin is not null)
                await stdin(process.StandardInput.BaseStream);
        }
        catch (IOException)
        {
            // o processo saiu antes de ler tudo (senha negada, por exemplo): o código de saída explica
        }
        finally
        {
            try { process.StandardInput.Close(); } catch (IOException) { }
        }
        await process.WaitForExitAsync(ct);
        await stdout;
        var error = await stderr;
        return (process.ExitCode, error.Length > 2000 ? error[^2000..] : error);
    }
}

/// <summary>
/// Instala, atualiza e remove a cópia do worker que vem dentro do AppImage (em
/// <see cref="WorkerLocator.AppImage"/>), sempre depois de a pessoa confirmar no app e digitar a senha
/// de administrador no diálogo do sistema.
/// </summary>
public sealed class BundledWorker(IPrivilegedShell shell, string? payloadDir, string root = "/")
{
    /// <summary>Arquivo que identifica o conteúdo do worker embutido (gravado por scripts/package-linux.sh).</summary>
    public const string IdFile = "PAYLOAD-ID";

    /// <summary>A política do polkit vai junto no .tar e o script a move para o lugar dela.</summary>
    public const string PolicyFile = "polkit.policy";

    /// <summary>
    /// Pasta do worker embutido: &lt;app&gt;/../worker-payload. Só existe no AppImage (o .deb instala o worker
    /// direto em /usr/lib/traderock).
    /// </summary>
    public static string? FindPayload(string baseDir)
    {
        var dir = Path.GetFullPath(Path.Combine(baseDir, "..", "worker-payload"));
        return File.Exists(Path.Combine(dir, IdFile)) ? dir : null;
    }

    /// <summary>
    /// Script que roda como root (packaging/linux/worker-setup.sh, embutido no assembly): $1 =
    /// install|uninstall, $2 = pasta do worker, $3 = arquivo da política. Na instalação, o .tar chega pela
    /// entrada padrão.
    /// </summary>
    public static string Script { get; } = LoadScript();

    private static string LoadScript()
    {
        using var stream = typeof(BundledWorker).Assembly.GetManifestResourceStream("worker-setup.sh")
            ?? throw new InvalidOperationException("worker-setup.sh não está embutido.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string? PayloadDir => payloadDir;

    private string R(string path) => Path.Combine(root, path.TrimStart('/'));

    public BundledWorkerStatus Status()
    {
        if (payloadDir is null)
            return BundledWorkerStatus.None;
        var target = WorkerLocator.AppImage;
        var installed = File.Exists(R(target.Launcher)) && File.Exists(R(target.Policy));
        var upToDate = installed && SameId(Path.Combine(payloadDir, IdFile), R(Path.Combine(target.Prefix, IdFile)));
        return new BundledWorkerStatus(true, installed, upToDate);
    }

    private static bool SameId(string a, string b)
    {
        try
        {
            return File.Exists(b) && File.ReadAllText(a).Trim() == File.ReadAllText(b).Trim();
        }
        catch (IOException)
        {
            return false;
        }
    }

    public async Task InstallAsync(CancellationToken ct)
    {
        if (payloadDir is null)
            throw new WorkerException("worker_not_bundled", "Esta versão do TradeRock não traz o componente de rede para instalar.");
        var target = WorkerLocator.AppImage;
        var (code, error) = await shell.RunAsync(Script, ["install", R(target.Prefix), R(target.Policy)],
            stream => TarFile.CreateFromDirectoryAsync(payloadDir, stream, includeBaseDirectory: false, ct), ct);
        Check(code, error);
    }

    public async Task UninstallAsync(CancellationToken ct)
    {
        var target = WorkerLocator.AppImage;
        var (code, error) = await shell.RunAsync(Script, ["uninstall", R(target.Prefix), R(target.Policy)], null, ct);
        Check(code, error);
    }

    /// <summary>Códigos de saída do pkexec (man pkexec): 126 = diálogo fechado, 127 = sem autorização.</summary>
    private static void Check(int code, string error)
    {
        switch (code)
        {
            case 0:
                return;
            case 126:
                throw new WorkerException("auth_cancelled", "A senha de administrador não foi informada.");
            case 127:
                throw new WorkerException("not_authorized", "O sistema não autorizou a instalação.");
            default:
                throw new WorkerException("worker_install_failed", $"A instalação falhou (código {code}): {error.Trim()}");
        }
    }
}
