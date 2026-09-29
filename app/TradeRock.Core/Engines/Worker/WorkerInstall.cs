using System.Runtime.InteropServices;

namespace TradeRock.Core.Engines.Worker;

/// <summary>
/// Onde o worker está instalado e se é seguro usá-lo.
/// <see cref="Safe"/> = o launcher e as pastas dele não podem ser alterados pelo usuário atual (se
/// pudessem, qualquer programa da sessão trocaria o código que o polkit roda como root).
/// </summary>
public sealed record WorkerInstall(
    string Flavor, // release | dev
    string Launcher,
    bool PolicyInstalled,
    bool Safe,
    bool PkexecFound,
    bool BundledTools = false); // iw e ip embutidos em <instalação>/bin

/// <summary>Instalações conhecidas do worker (scripts/install-worker.sh e, na Entrega 8, os pacotes).</summary>
public static class WorkerLocator
{
    public sealed record Candidate(string Flavor, string Prefix, string ActionId)
    {
        public string Launcher => Path.Combine(Prefix, "traderock-worker");
        public string Policy => $"/usr/share/polkit-1/actions/{ActionId}.policy";
    }

    public static IReadOnlyList<Candidate> Candidates { get; } =
    [
        new("release", "/usr/lib/traderock", "io.github.traderock.worker"),
        new("dev", "/usr/local/lib/traderock-dev", "io.github.traderock.worker.dev"),
    ];

    /// <summary>Ferramentas que o worker traz prontas (scripts/build-nettools.sh).</summary>
    public static readonly string[] BundledTools = ["iw", "ip"];

    public static readonly string[] PkexecPaths = ["/usr/bin/pkexec", "/bin/pkexec"];

    /// <param name="root">Raiz do sistema de arquivos (testes usam uma pasta falsa).</param>
    /// <param name="preferDev">Em desenvolvimento, a instalação dev vem antes da release.</param>
    public static WorkerInstall? Find(string root = "/", bool preferDev = false, Func<string, bool>? writable = null)
    {
        writable ??= IsWritableByMe;
        string R(string path) => Path.Combine(root, path.TrimStart('/'));

        var order = preferDev ? Candidates.Reverse() : Candidates;
        foreach (var c in order)
        {
            if (!File.Exists(R(c.Launcher)))
                continue;
            var safe = !writable(R(c.Launcher)) && AncestorsOf(c.Launcher).All(d => !writable(R(d)));
            return new WorkerInstall(c.Flavor, c.Launcher, File.Exists(R(c.Policy)), safe,
                PkexecPaths.Any(p => File.Exists(R(p))),
                BundledTools.All(t => File.Exists(R(Path.Combine(c.Prefix, "bin", t)))));
        }
        return null;
    }

    public static string? PkexecPath(string root = "/") =>
        PkexecPaths.FirstOrDefault(p => File.Exists(Path.Combine(root, p.TrimStart('/'))));

    private static IEnumerable<string> AncestorsOf(string path)
    {
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir) && dir != "/"; dir = Path.GetDirectoryName(dir))
            yield return dir;
    }

    private const int W_OK = 2;

    [DllImport("libc", SetLastError = true, EntryPoint = "access")]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    /// <summary>O usuário atual consegue escrever no caminho? (access(2), como o kernel decide.)</summary>
    public static bool IsWritableByMe(string path) =>
        OperatingSystem.IsLinux() && Access(path, W_OK) == 0;
}
