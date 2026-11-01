using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TradeRock.Core.Logging;
using TradeRock.Core.Esp32;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>Processo do pokeldn (worker/traderock_worker/pokeldn_board.py e pokeldn_trade.py); os testes usam um falso.</summary>
public interface IPokeldnProcess
{
    bool Available { get; }

    /// <summary>
    /// Roda `run.py --pokeldn <paramref name="mode"/>` com o pedido na entrada e devolve as mensagens JSON da saída.
    /// Cancelar pede a saída educada (a linha "stop" na entrada, que o pokeldn_trade.py trata como o Ctrl+C do
    /// pokeldn, no Linux e no Windows; D30) e, sem resposta em <see cref="PokeldnProcess.GracefulExit"/>, encerra o
    /// processo.
    /// </summary>
    IAsyncEnumerable<JsonObject> RunAsync(string mode, JsonObject request, CancellationToken ct);
}

/// <summary>
/// O pokeldn no Python embutido, como o usuário (ESP32, sem pkexec; D23). O runtime é o mesmo do esptool: o worker
/// do pacote ou build/worker/stage no Linux, esp32-runtime ao lado do app no Windows.
/// </summary>
public sealed class PokeldnProcess(Func<string?> runtimeRoot, string tempDir, ILogger? logger = null) : IPokeldnProcess
{
    /// <summary>Tempo para o pokeldn sair da sala com educação antes de ser encerrado.</summary>
    public static readonly TimeSpan GracefulExit = TimeSpan.FromSeconds(30);

    /// <summary>A linha que pede a saída educada (pokeldn_trade.py, watch_stop).</summary>
    public const string StopLine = "stop";

    public bool Available => runtimeRoot() is { } root && Directory.Exists(Path.Combine(root, "pokeldn"));

    public async IAsyncEnumerable<JsonObject> RunAsync(string mode, JsonObject request, [EnumeratorCancellation] CancellationToken ct)
    {
        var root = runtimeRoot() is { } r && Directory.Exists(Path.Combine(r, "pokeldn"))
            ? r
            : throw new Esp32Exception("pokeldn_missing", "O pokeldn embutido não foi encontrado.");
        var start = PythonEsp32Flasher.StartInfo(root, tempDir, "--pokeldn", mode);
        using var process = Process.Start(start) ?? throw new Esp32Exception("pokeldn_missing", "Não foi possível iniciar o pokeldn.");
        // Só a troca e as atividades (pokeldn_trade.py) ouvem o "stop"; o resto (pokeldn_board.py) termina na hora.
        using var stop = ct.Register(() => _ = StopAsync(process, graceful: mode == "trade"));
        // O log do pokeldn (já sem chaves de sessão) vai para a aba Logs (D28) quando o processo termina.
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        // A entrada fica aberta até o fim: é por ela que o cancelamento pede a saída educada (D30). O pokeldn_trade.py
        // só lê a primeira linha (o pedido) e a palavra "stop"; o fim da entrada não interrompe nada.
        await process.StandardInput.WriteLineAsync(request.ToJsonString());
        await process.StandardInput.FlushAsync();

        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (TryParse(line) is { } message)
                yield return message;
        }
        await process.WaitForExitAsync(CancellationToken.None);
        ProcessLog.Write(logger, $"pokeldn {mode}", process.ExitCode, await stderr);
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

    private static async Task StopAsync(Process process, bool graceful)
    {
        try
        {
            if (process.HasExited)
                return;
            if (!graceful)
            {
                process.Kill(entireProcessTree: true);
                return;
            }
            // "stop" na entrada: o pokeldn passa a sala ao console e sai (D21: sair no meio castiga o console). Vale
            // nos dois sistemas; no Windows não há SIGINT para outro processo.
            if (await TryStopLineAsync(process))
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

    private static async Task<bool> TryStopLineAsync(Process process)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(StopLine);
            await process.StandardInput.FlushAsync();
            return true;
        }
        catch (IOException)
        {
            return false; // a entrada já fechou: o processo está saindo
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
