using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using TradeRock.Core.Esp32;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>Processo do pokeldn (worker/traderock_worker/pokeldn_board.py e pokeldn_trade.py); os testes usam um falso.</summary>
public interface IPokeldnProcess
{
    bool Available { get; }

    /// <summary>
    /// Roda `run.py --pokeldn <paramref name="mode"/>` com o pedido na entrada e devolve as mensagens JSON da saída.
    /// Cancelar pede a saída educada (SIGINT no Linux, como o Ctrl+C do pokeldn) e, sem resposta em
    /// <see cref="PokeldnProcess.GracefulExit"/>, encerra o processo.
    /// </summary>
    IAsyncEnumerable<JsonObject> RunAsync(string mode, JsonObject request, CancellationToken ct);
}

/// <summary>
/// O pokeldn no Python embutido, como o usuário (ESP32, sem pkexec; D23). O runtime é o mesmo do esptool: o worker
/// do pacote ou build/worker/stage no Linux, esp32-runtime ao lado do app no Windows.
/// </summary>
public sealed class PokeldnProcess(Func<string?> runtimeRoot, string tempDir) : IPokeldnProcess
{
    /// <summary>Tempo para o pokeldn sair da sala com educação antes de ser encerrado.</summary>
    public static readonly TimeSpan GracefulExit = TimeSpan.FromSeconds(20);

    public bool Available => runtimeRoot() is { } root && Directory.Exists(Path.Combine(root, "pokeldn"));

    public async IAsyncEnumerable<JsonObject> RunAsync(string mode, JsonObject request, [EnumeratorCancellation] CancellationToken ct)
    {
        var root = runtimeRoot() is { } r && Directory.Exists(Path.Combine(r, "pokeldn"))
            ? r
            : throw new Esp32Exception("pokeldn_missing", "O pokeldn embutido não foi encontrado.");
        var start = PythonEsp32Flasher.StartInfo(root, tempDir, "--pokeldn", mode);
        using var process = Process.Start(start) ?? throw new Esp32Exception("pokeldn_missing", "Não foi possível iniciar o pokeldn.");
        using var stop = ct.Register(() => _ = StopAsync(process));
        // O log do pokeldn (já sem chaves de sessão) só é lido para não travar o processo.
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.StandardInput.WriteLineAsync(request.ToJsonString());
        process.StandardInput.Close();

        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (TryParse(line) is { } message)
                yield return message;
        }
        await process.WaitForExitAsync(CancellationToken.None);
        await stderr;
    }

    private static JsonObject? TryParse(string line)
    {
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (process.HasExited)
                return;
            // Linux: SIGINT, o mesmo do Ctrl+C, para o pokeldn passar a sala e sair (D21: sair no meio castiga o
            // console). No Windows não há SIGINT para outro processo: encerra direto.
            if (!OperatingSystem.IsWindows() && Kill(process.Id, SigInt) == 0)
            {
                using var wait = new CancellationTokenSource(GracefulExit);
                try
                {
                    await process.WaitForExitAsync(wait.Token);
                    return;
                }
                catch (OperationCanceledException)
                {
                }
            }
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // já saiu
        }
    }

    private const int SigInt = 2;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
