using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Logging;

namespace TradeRock.Core.Esp32;

/// <summary>Chip lido pelo esptool (sem gravar nada).</summary>
public sealed record Esp32ChipReading(string Target, string Description, int Revision, string? FlashSize);

/// <summary>Resultado de conferir ou gravar o firmware.</summary>
public sealed record Esp32FlashOutcome(Esp32ChipReading Chip, bool Matches, bool Written);

public sealed record Esp32Progress(string Stage, int Done, int Total);

/// <summary>esptool: identificar o chip, conferir e gravar o firmware. Os testes usam um falso.</summary>
public interface IEsp32Flasher
{
    bool Available { get; }

    Task<Esp32ChipReading> IdentifyAsync(string port, CancellationToken ct);

    Task<Esp32FlashOutcome> VerifyAsync(string port, FirmwareImage image, CancellationToken ct);

    Task<Esp32FlashOutcome> FlashAsync(string port, FirmwareImage image, Action<Esp32Progress> progress, CancellationToken ct);
}

/// <summary>
/// Roda worker/traderock_worker/esp32_flash.py (API do esptool 5) com o Python embutido, como o
/// usuário comum (sem pkexec): a porta serial só precisa do grupo dialout/uucp.
/// </summary>
public sealed class PythonEsp32Flasher(Func<string?> runtimeRoot, string tempDir, ILogger? logger = null) : IEsp32Flasher
{
    /// <summary>
    /// Onde está o runtime (pasta com python/ e worker/): o worker embutido no AppImage (legível pelo
    /// usuário), o worker instalado (.deb, AppImage, dev) ou, em desenvolvimento, build/worker/stage.
    /// </summary>
    public static string? FindRuntime(string? appImagePayload, bool preferDev, string? devStage)
    {
        // Windows: o instalador põe o runtime ao lado do app (scripts/package-windows.sh).
        if (OperatingSystem.IsWindows())
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, WindowsRuntimeDir);
            return IsRuntime(bundled) ? bundled : null;
        }

        var candidates = new List<string?> { appImagePayload };
        var installs = WorkerLocator.Candidates.Select(c => c.Prefix).ToList();
        if (preferDev)
            installs = [.. installs.OrderBy(p => p == WorkerLocator.Dev.Prefix ? 0 : 1)];
        candidates.AddRange(installs);
        candidates.Add(devStage);
        return candidates.FirstOrDefault(root => root is not null && IsRuntime(root));
    }

    /// <summary>Pasta do runtime do esptool no Windows, ao lado do traderock.exe.</summary>
    public const string WindowsRuntimeDir = "esp32-runtime";

    /// <summary>python/bin/python3 (Linux) ou python/python.exe (Windows, python-build-standalone).</summary>
    public static string PythonPath(string root) => OperatingSystem.IsWindows()
        ? Path.Combine(root, "python", "python.exe")
        : Path.Combine(root, "python", "bin", "python3");

    private static bool IsRuntime(string root) =>
        File.Exists(PythonPath(root)) && File.Exists(Path.Combine(root, "worker", "run.py"));

    public bool Available => runtimeRoot() is not null;

    public async Task<Esp32ChipReading> IdentifyAsync(string port, CancellationToken ct)
    {
        var result = await RunAsync(new JsonObject { ["op"] = "identify", ["port"] = port }, null, ct);
        return ReadChip(result);
    }

    public Task<Esp32FlashOutcome> VerifyAsync(string port, FirmwareImage image, CancellationToken ct) =>
        WithImageAsync(image, path => RunAsync(ImageRequest("verify", port, image, path), null, ct));

    public Task<Esp32FlashOutcome> FlashAsync(string port, FirmwareImage image, Action<Esp32Progress> progress, CancellationToken ct) =>
        WithImageAsync(image, path => RunAsync(ImageRequest("flash", port, image, path), progress, ct));

    private static JsonObject ImageRequest(string op, string port, FirmwareImage image, string path) => new()
    {
        ["op"] = op,
        ["port"] = port,
        ["image"] = path,
        ["flashSize"] = FirmwareImage.FlashSizeParam,
        ["flashMode"] = FirmwareImage.FlashMode,
        ["flashFreq"] = FirmwareImage.FlashFreq,
    };

    private async Task<Esp32FlashOutcome> WithImageAsync(FirmwareImage image, Func<string, Task<JsonObject>> run)
    {
        // Pasta própria por operação: o binário conferido existe só enquanto o esptool roda.
        var dir = Path.Combine(tempDir, "esp32-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Esp32Firmware.ExtractTo(image, dir);
            var result = await run(path);
            return new Esp32FlashOutcome(ReadChip(result), result["matches"]?.GetValue<bool>() ?? false, result["written"]?.GetValue<bool>() ?? false);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static Esp32ChipReading ReadChip(JsonObject r) => new(
        r["target"]?.GetValue<string>() ?? "?",
        r["description"]?.GetValue<string>() ?? "",
        r["revision"]?.GetValue<int>() ?? 0,
        r["flashSize"]?.GetValue<string>());

    /// <summary>
    /// O Python embutido rodando worker/run.py com <paramref name="args"/>, como o usuário, com ambiente mínimo:
    /// pedido e respostas em JSON UTF-8 pela entrada e saída padrão (esptool e pokeldn).
    /// </summary>
    public static ProcessStartInfo StartInfo(string root, string tempDir, params string[] args)
    {
        var start = new ProcessStartInfo(PythonPath(root))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true, // Windows: sem janela de console piscando
            // O pedido e as respostas são JSON em UTF-8 (caminhos com acento no Windows).
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in new[] { "-I", "-B", Path.Combine(root, "worker", "run.py") }.Concat(args))
            start.ArgumentList.Add(arg);
        start.Environment.Clear();
        Directory.CreateDirectory(tempDir);
        if (OperatingSystem.IsWindows())
        {
            // O Python do Windows precisa do SystemRoot (criptografia, sockets) e de uma pasta temporária.
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            start.Environment["SystemRoot"] = systemRoot;
            start.Environment["PATH"] = Path.Combine(systemRoot, "System32");
            start.Environment["TEMP"] = start.Environment["TMP"] = Path.GetTempPath();
            // Sem a pasta pessoal o Python falha ("Could not determine home directory") ao carregar a
            // configuração do esptool. Aponta para uma pasta do próprio TradeRock, vazia, para que o
            // esptool também não leia arquivos de configuração soltos do usuário.
            start.Environment["USERPROFILE"] = tempDir;
        }
        else
        {
            start.Environment["PATH"] = "/usr/bin:/bin";
            start.Environment["LANG"] = "C.UTF-8";
            start.Environment["HOME"] = tempDir; // idem no Linux (esptool e pokeldn)
        }
        return start;
    }

    private async Task<JsonObject> RunAsync(JsonObject request, Action<Esp32Progress>? progress, CancellationToken ct)
    {
        var root = runtimeRoot() ?? throw new Esp32Exception("esptool_missing", "O esptool embutido não foi encontrado.");
        var start = StartInfo(root, tempDir, "--esp32");

        using var process = Process.Start(start) ?? throw new Esp32Exception("esptool_missing", "Não foi possível iniciar o esptool.");
        using var kill = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.StandardInput.WriteLineAsync(request.ToJsonString());
        process.StandardInput.Close();

        JsonObject? result = null;
        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (JsonNode.Parse(line) is not JsonObject msg)
                continue;
            switch (msg["type"]?.GetValue<string>())
            {
                case "progress":
                    progress?.Invoke(new Esp32Progress(msg["stage"]?.GetValue<string>() ?? "", msg["done"]?.GetValue<int>() ?? 0, msg["total"]?.GetValue<int>() ?? 0));
                    break;
                case "result":
                    result = msg;
                    break;
                case "error":
                    throw new Esp32Exception(msg["code"]?.GetValue<string>() ?? "esptool_error", msg["message"]?.GetValue<string>() ?? "");
            }
        }
        await process.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        var log = await stderr;
        ProcessLog.Write(logger, $"esptool {request["op"]}", process.ExitCode, log);
        return result ?? throw new Esp32Exception("esptool_error", log.Length > 500 ? log[^500..] : log);
    }
}
