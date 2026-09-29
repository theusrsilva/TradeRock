using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Pokemon;

namespace TradeRock.Tests;

/// <summary>
/// Worker falso que fala o protocolo de worker/traderock_worker/server.py por pipes em memória.
/// Serve para testar o app sem root, sem pkexec e sem placa.
/// </summary>
public sealed class FakeWorker : IWorkerLauncher
{
    public WorkerInstall? Installed { get; set; } = new("dev", "/usr/local/lib/traderock-dev/traderock-worker", true, true, true);

    /// <summary>Simula o pkexec saindo antes do worker responder (126 = diálogo fechado, 127 = negado).</summary>
    public int? PkexecExitCode { get; set; }

    /// <summary>success | hang (espera o cancelamento) | no_network | card_fail</summary>
    public string Mode { get; set; } = "success";

    public int Launches;
    public ConcurrentQueue<JsonObject> Requests { get; } = new();

    public WorkerInstall? Find() => Installed;

    public Task<IWorkerConnection> LaunchAsync(WorkerInstall install, CancellationToken ct)
    {
        Interlocked.Increment(ref Launches);
        var connection = new Connection();
        _ = Task.Run(() => RunAsync(connection));
        return Task.FromResult<IWorkerConnection>(connection);
    }

    private sealed class Connection : IWorkerConnection
    {
        public readonly Pipe ToWorkerPipe = new();
        public readonly Pipe FromWorkerPipe = new();
        public readonly TaskCompletionSource<int> Exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream ToWorker => ToWorkerPipe.Writer.AsStream();
        public Stream FromWorker => FromWorkerPipe.Reader.AsStream();
        public Task<int> Exited => Exit.Task;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private async Task RunAsync(Connection c)
    {
        var output = c.FromWorkerPipe.Writer.AsStream();
        var writeLock = new SemaphoreSlim(1, 1);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Send(JsonObject msg)
        {
            await writeLock.WaitAsync();
            try
            {
                await output.WriteAsync(Encoding.UTF8.GetBytes(msg.ToJsonString() + "\n"));
                await output.FlushAsync();
            }
            finally
            {
                writeLock.Release();
            }
        }

        Task Reply(JsonNode? id, JsonObject? result = null) =>
            Send(new JsonObject { ["id"] = id?.DeepClone(), ["ok"] = true, ["result"] = result ?? [] });

        using var reader = new StreamReader(c.ToWorkerPipe.Reader.AsStream(), Encoding.UTF8);
        while (await reader.ReadLineAsync() is { } line)
        {
            var msg = JsonNode.Parse(line)!.AsObject();
            Requests.Enqueue(msg);
            var id = msg["id"];
            switch (msg["op"]!.GetValue<string>())
            {
                case "hello":
                    if (PkexecExitCode is { } code)
                    {
                        await c.FromWorkerPipe.Writer.CompleteAsync();
                        c.Exit.TrySetResult(code);
                        return;
                    }
                    await Reply(id, new JsonObject
                    {
                        ["version"] = "0.6.0", ["protocol"] = 1, ["root"] = true,
                        ["engineCommit"] = "13809c2", ["missingTools"] = new JsonArray(),
                    });
                    break;
                case "card_test":
                    var passed = Mode != "card_fail";
                    await Reply(id, new JsonObject
                    {
                        ["monitor"] = passed,
                        ["channels"] = new JsonArray(new JsonObject { ["channel"] = 1, ["set"] = true, ["sent"] = passed ? 5 : 0, ["received"] = 7 }),
                        ["passed"] = passed,
                        ["code"] = passed ? "passed" : "monitor_failed",
                    });
                    break;
                case "net_install":
                    await Reply(id, new JsonObject { ["networkManager"] = true, ["udevLink"] = true });
                    break;
                case "cancel":
                    cancelled.TrySetResult();
                    await Reply(id, new JsonObject { ["cancelling"] = true });
                    break;
                case "start_trade":
                    await Reply(id, new JsonObject { ["started"] = true });
                    var slots = msg["slots"]!.AsArray().Count;
                    _ = Task.Run(() => TradeAsync(Send, slots, cancelled.Task));
                    break;
                default:
                    await Send(new JsonObject { ["id"] = id?.DeepClone(), ["ok"] = false, ["error"] = new JsonObject { ["code"] = "unknown_op" } });
                    break;
            }
        }
        await c.FromWorkerPipe.Writer.CompleteAsync();
        c.Exit.TrySetResult(0);
    }

    private async Task TradeAsync(Func<JsonObject, Task> send, int trades, Task cancelled)
    {
        Task Event(string kind, int? index = null, string? data = null) => send(new JsonObject
        {
            ["event"] = "trade", ["kind"] = kind, ["message"] = kind, ["tradeIndex"] = index, ["data"] = data,
        });

        await Event("scanning");
        if (Mode == "no_network")
        {
            await send(new JsonObject { ["event"] = "trade_end", ["outcome"] = "failed", ["code"] = "no_network_found", ["retryable"] = true, ["received"] = 0 });
            return;
        }
        await Event("beacon_decrypted");
        await Event("join_requested");
        if (Mode == "hang")
        {
            await cancelled;
            await Event("log");
            await send(new JsonObject { ["event"] = "trade_end", ["outcome"] = "cancelled", ["code"] = "cancelled", ["received"] = 0 });
            return;
        }
        await Event("joined");
        await Event("in_room");
        await Event("trade_menu_open");
        for (var i = 0; i < trades; i++)
        {
            await Event("trade_offered", i);
            await Event("confirming", i);
            var mon = DemoPokemon.All[i % DemoPokemon.All.Count].Data;
            await Event("trade_done", i, Convert.ToBase64String(mon));
        }
        await Event("leaving");
        await Event("link_closed");
        await send(new JsonObject { ["event"] = "trade_end", ["outcome"] = "completed", ["received"] = trades });
    }
}
