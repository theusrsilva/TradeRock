using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace TradeRock.Core.Engines.Worker;

/// <summary>Erro do worker, ou de chegar até ele. <see cref="Code"/> é estável (traduzido no frontend).</summary>
public sealed class WorkerException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Os dois lados do canal com o worker (entrada/saída padrão do processo do pkexec).</summary>
public interface IWorkerConnection : IAsyncDisposable
{
    Stream ToWorker { get; }
    Stream FromWorker { get; }

    /// <summary>Completa com o código de saída quando o processo termina.</summary>
    Task<int> Exited { get; }
}

/// <summary>
/// Protocolo do worker (worker/traderock_worker/server.py): uma linha JSON por mensagem.
/// Respostas casam com o pedido pelo "id"; eventos ("trade", "trade_end") vão para <see cref="Events"/>;
/// "debug" vai para o log.
/// </summary>
public sealed class WorkerClient : IAsyncDisposable
{
    public const int Protocol = 1;

    private readonly IWorkerConnection _connection;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Channel<JsonObject> _events = Channel.CreateUnbounded<JsonObject>(new() { SingleReader = true });
    private readonly Task _readLoop;
    private long _nextId;

    public WorkerClient(IWorkerConnection connection, ILogger logger)
    {
        _connection = connection;
        _logger = logger;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public ChannelReader<JsonObject> Events => _events.Reader;

    /// <summary>O worker saiu (ou o canal caiu).</summary>
    public bool IsClosed { get; private set; }

    public Task<int> Exited => _connection.Exited;

    public async Task<JsonObject> RequestAsync(string op, JsonObject? args, TimeSpan timeout, CancellationToken ct)
    {
        if (IsClosed)
            throw new WorkerException("worker_exited", "O componente de rede não está mais rodando.");

        var id = Interlocked.Increment(ref _nextId);
        var message = args?.DeepClone().AsObject() ?? [];
        message["id"] = id;
        message["op"] = op;
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            var line = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
            await _writeLock.WaitAsync(ct);
            try
            {
                await _connection.ToWorker.WriteAsync(line, ct);
                await _connection.ToWorker.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }

            var reply = await tcs.Task.WaitAsync(timeout, ct);
            if (reply["ok"]?.GetValue<bool>() == true)
                return reply["result"]?.AsObject() ?? [];
            var error = reply["error"]?.AsObject();
            throw new WorkerException(error?["code"]?.GetValue<string>() ?? "worker_error",
                error?["message"]?.GetValue<string>() ?? "Erro do componente de rede.");
        }
        catch (TimeoutException)
        {
            throw new WorkerException("worker_timeout", $"O componente de rede não respondeu a tempo ({op}).");
        }
        catch (IOException)
        {
            throw new WorkerException("worker_exited", "O componente de rede não está mais rodando.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            using var reader = new StreamReader(_connection.FromWorker, Encoding.UTF8);
            while (await reader.ReadLineAsync() is { } line)
            {
                JsonObject? msg;
                try
                {
                    msg = JsonNode.Parse(line) as JsonObject;
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Linha inválida do worker ignorada.");
                    continue;
                }
                if (msg is null)
                    continue;

                if (msg["event"]?.GetValue<string>() is { } kind)
                {
                    if (kind == "debug")
                        _logger.LogDebug("worker: {Text}", msg["text"]?.GetValue<string>());
                    else
                        _events.Writer.TryWrite(msg);
                }
                else if (msg["id"] is JsonValue idValue && idValue.TryGetValue<long>(out var id)
                         && _pending.TryGetValue(id, out var tcs))
                {
                    tcs.TrySetResult(msg);
                }
                else if (msg["error"] is JsonObject error)
                {
                    _logger.LogWarning("Worker: {Code} {Message}", error["code"], error["message"]);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // canal fechado
        }
        finally
        {
            IsClosed = true;
            var exited = new WorkerException("worker_exited", "O componente de rede parou.");
            foreach (var tcs in _pending.Values)
                tcs.TrySetException(exited);
            _events.Writer.TryWrite(new JsonObject { ["event"] = "worker_exited" });
            _events.Writer.TryComplete();
        }
    }

    /// <summary>Descarta eventos velhos (de uma troca anterior) antes de começar outra.</summary>
    public void DrainEvents()
    {
        while (_events.Reader.TryRead(out _)) { }
    }

    /// <summary>
    /// Fecha a entrada do worker: ele cancela o que estiver rodando, devolve o rádio ao estado anterior
    /// e sai. Espera até <paramref name="wait"/> por isso.
    /// </summary>
    public async Task CloseAsync(TimeSpan wait)
    {
        try
        {
            await _connection.ToWorker.DisposeAsync();
        }
        catch (IOException)
        {
        }
        await Task.WhenAny(_connection.Exited, Task.Delay(wait));
        await Task.WhenAny(_readLoop, Task.Delay(TimeSpan.FromSeconds(2)));
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(TimeSpan.FromSeconds(60));
        await _connection.DisposeAsync();
        _writeLock.Dispose();
    }
}
