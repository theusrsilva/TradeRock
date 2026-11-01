using System.Text.Json.Nodes;
using TradeRock.Core.Activities;
using TradeRock.Core.Engines.Pokeldn;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Games;
using TradeRock.Core.Setup;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Engines;

public sealed class PythonLdnEngineOptions
{
    /// <summary>
    /// Quanto o motor espera a pessoa aprovar a entrada no Switch (FRLG_JOIN_IP_WAIT do patch).
    /// Um pouco menos que o limite do coordenador (TradeSettings.ApprovalTimeoutSeconds), para o motor
    /// desistir primeiro com um motivo claro.
    /// </summary>
    public int ApprovalSeconds { get; init; } = 100;

    /// <summary>Depois de pedir o cancelamento, quanto esperar o worker sair da sala e devolver o rádio.</summary>
    public TimeSpan CancelWait { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Motor com a placa Wi-Fi do PC (só Linux): o frlg-ldn-trade roda no worker privilegiado, que
/// devolve os marcos da troca como eventos. Uma chamada = uma tentativa (o retry é do coordenador).
/// </summary>
public sealed class PythonLdnEngine(WorkerHost worker, SetupService setup, ProdKeys keys, PythonLdnEngineOptions options) : ITradeEngine, IActivityEngine
{
    public const string EngineId = "python-ldn";

    public string Id => EngineId;

    public bool IsSimulation => false;

    /// <summary>FRLG pelo frlg-ldn-trade (o console é o líder); os outros jogos pelo pokeldn, no worker (D23).</summary>
    public bool Supports(GameFamily family) => true;

    public bool UsesPokeldn(GameFamily family) => family != GameFamily.Frlg;

    public async Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            return new EngineAvailability(false, "not_linux");
        if (worker.Install is null)
            return new EngineAvailability(false, "worker_missing");
        var state = (await setup.StatusAsync(cancellationToken)).State;
        if (state.Radio != RadioAdvisor.Wifi || state.Phy is null)
            return new EngineAvailability(false, "wifi_not_configured");
        return new EngineAvailability(true);
    }

    public async Task<TradeAttemptResult> RunAttemptAsync(TradeRequest request, Func<TradeEvent, ValueTask> emit, CancellationToken cancellationToken)
    {
        var state = (await setup.StatusAsync(cancellationToken)).State;
        if (state.Phy is null)
            return TradeAttemptResult.Fail("wifi_not_configured", "Escolha a placa Wi-Fi no assistente.", retryable: false);
        // Os jogos novos pedem a sala aberta pela placa (modo AP, D23); o teste da placa diz se ela consegue.
        if (request.GameInfo.Family != GameFamily.Frlg && state.CardTest is { Ap: false } card && card.Phy == state.Phy)
            return TradeAttemptResult.Fail("card_no_ap", "Esta placa não abre a sala dos jogos novos (modo AP).", retryable: false);

        byte[] keyFile;
        try
        {
            keyFile = await File.ReadAllBytesAsync(keys.FilePath, cancellationToken);
        }
        catch (IOException)
        {
            return TradeAttemptResult.Fail("keys_missing", "O prod.keys não foi enviado.", retryable: false);
        }

        WorkerClient client;
        try
        {
            client = await worker.GetAsync(cancellationToken);
        }
        catch (WorkerException e)
        {
            return TradeAttemptResult.Fail(e.Code, e.Message, retryable: false);
        }

        client.DrainEvents();
        if (request.GameInfo.Family != GameFamily.Frlg)
            return await RunPokeldnAsync(client, request, state.Phy, keyFile, emit, cancellationToken);
        var args = new JsonObject
        {
            ["phy"] = state.Phy,
            ["keys"] = Convert.ToBase64String(keyFile),
            ["party"] = new JsonArray(request.Party.Select(p => (JsonNode)Convert.ToBase64String(p.Data)).ToArray()),
            ["slots"] = new JsonArray(request.OfferedSlots.Select(s => (JsonNode)s).ToArray()),
            ["game"] = request.Game == TradeGame.FireRed ? "firered" : "leafgreen",
            ["name"] = request.Trainer.Name,
            ["tid"] = request.Trainer.Tid,
            ["sid"] = request.Trainer.Sid is { } sid ? sid : null,
            ["approvalSeconds"] = options.ApprovalSeconds,
        };
        Array.Clear(keyFile);

        try
        {
            await client.RequestAsync("start_trade", args, TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (WorkerException e)
        {
            return TradeAttemptResult.Fail(e.Code, e.Message, retryable: e.Code is "busy" or "worker_timeout");
        }
        finally
        {
            args["keys"] = null;
        }

        return await FollowAsync(client, emit, cancellationToken);
    }

    /// <summary>Jogos do Switch pelo pokeldn com a placa Wi-Fi: o worker (root) roda o pokeldn_trade.py com a placa.</summary>
    private async Task<TradeAttemptResult> RunPokeldnAsync(WorkerClient client, TradeRequest request, string phy, byte[] keyFile,
        Func<TradeEvent, ValueTask> emit, CancellationToken ct)
    {
        var progress = new PokeldnEngine.Progress(request);
        try
        {
            var end = await StreamPokeldnAsync(client, phy, keyFile, PokeldnEngine.Request(request, radio: null, keysPath: null),
                async message =>
                {
                    if (progress.Map(message, DateTimeOffset.UtcNow) is { } e)
                        await emit(e);
                }, ct);
            return end switch
            {
                PokeldnEnd.Finished => progress.Result(),
                PokeldnEnd.FinishedAfterCancel => progress.Result(cancelled: true),
                PokeldnEnd.WorkerExited => TradeAttemptResult.Fail("worker_exited", "O componente de rede parou durante a troca.", retryable: false),
                _ => TradeAttemptResult.Cancelled,
            };
        }
        catch (WorkerException e)
        {
            return TradeAttemptResult.Fail(e.Code, e.Message, retryable: e.Code is "busy" or "worker_timeout");
        }
    }

    private enum PokeldnEnd
    {
        Finished,
        FinishedAfterCancel,
        WorkerExited,
        Cancelled,
    }

    /// <summary>
    /// Pede ao worker o pokeldn_trade.py com a placa e repassa as mensagens dele até o fim. Cancelado: pede ao worker
    /// e espera ele sair da sala. As chaves saem da memória assim que o pedido é enviado.
    /// </summary>
    private async Task<PokeldnEnd> StreamPokeldnAsync(WorkerClient client, string phy, byte[] keyFile, JsonObject request,
        Func<JsonObject, ValueTask> onMessage, CancellationToken ct)
    {
        var args = new JsonObject
        {
            ["phy"] = phy,
            ["keys"] = Convert.ToBase64String(keyFile),
            ["request"] = request,
        };
        Array.Clear(keyFile);
        try
        {
            await client.RequestAsync("start_pokeldn", args, TimeSpan.FromSeconds(30), ct);
        }
        finally
        {
            args["keys"] = null;
        }

        var cancelRequested = false;
        using var cancelDeadline = new CancellationTokenSource();
        await using var registration = ct.Register(() =>
        {
            cancelRequested = true;
            _ = client.RequestAsync("cancel", null, TimeSpan.FromSeconds(10), CancellationToken.None)
                .ContinueWith(_ => { }, TaskScheduler.Default);
            cancelDeadline.CancelAfter(options.CancelWait);
        });
        try
        {
            await foreach (var msg in client.Events.ReadAllAsync(cancelDeadline.Token))
            {
                switch (msg["event"]?.GetValue<string>())
                {
                    case "pokeldn" when msg["message"] is JsonObject message:
                        await onMessage(message);
                        break;
                    case "pokeldn_end":
                        return cancelRequested ? PokeldnEnd.FinishedAfterCancel : PokeldnEnd.Finished;
                    case "worker_exited":
                        return cancelRequested ? PokeldnEnd.Cancelled : PokeldnEnd.WorkerExited;
                }
            }
        }
        catch (OperationCanceledException) when (cancelDeadline.IsCancellationRequested)
        {
        }
        return PokeldnEnd.Cancelled;
    }

    public bool SupportsActivity(GameFamily family, ActivityKind kind) => PokeldnActivity.Supports(family, kind);

    /// <summary>Outras atividades (D26) com a placa Wi-Fi, pelo worker, como os jogos novos.</summary>
    public async Task<ActivityResult> RunActivityAsync(ActivityRequest request, Func<ActivityEvent, ValueTask> emit, CancellationToken ct)
    {
        var state = (await setup.StatusAsync(ct)).State;
        if (state.Phy is null)
            return ActivityResult.Fail("wifi_not_configured");
        // A placa abre a sala (modo AP) nas atividades de todos os jogos, o FRLG inclusive (D23, D25).
        if (state.CardTest is { Ap: false } card && card.Phy == state.Phy)
            return ActivityResult.Fail("card_no_ap");
        byte[] keyFile;
        try
        {
            keyFile = await File.ReadAllBytesAsync(keys.FilePath, ct);
        }
        catch (IOException)
        {
            return ActivityResult.Fail("keys_missing");
        }

        var progress = new PokeldnActivity.Progress(request);
        try
        {
            var client = await worker.GetAsync(ct);
            client.DrainEvents();
            var end = await StreamPokeldnAsync(client, state.Phy, keyFile, PokeldnActivity.Request(request, radio: null, keysPath: null),
                async message =>
                {
                    if (progress.Map(message, DateTimeOffset.UtcNow) is { } e)
                        await emit(e);
                }, ct);
            return end == PokeldnEnd.WorkerExited ? ActivityResult.Fail("worker_exited") : progress.Result(end != PokeldnEnd.Finished);
        }
        catch (WorkerException e)
        {
            return ActivityResult.Fail(e.Code, e.Message);
        }
        finally
        {
            Array.Clear(keyFile);
        }
    }

    private async Task<TradeAttemptResult> FollowAsync(WorkerClient client, Func<TradeEvent, ValueTask> emit, CancellationToken ct)
    {
        var cancelRequested = false;
        using var cancelDeadline = new CancellationTokenSource();
        await using var registration = ct.Register(() =>
        {
            cancelRequested = true;
            _ = client.RequestAsync("cancel", null, TimeSpan.FromSeconds(10), CancellationToken.None)
                .ContinueWith(_ => { }, TaskScheduler.Default);
            cancelDeadline.CancelAfter(options.CancelWait);
        });

        try
        {
            await foreach (var msg in client.Events.ReadAllAsync(cancelDeadline.Token))
            {
                switch (msg["event"]?.GetValue<string>())
                {
                    case "trade":
                        if (ToEvent(msg) is { } tradeEvent)
                            await emit(tradeEvent);
                        break;
                    case "trade_end":
                        return ToResult(msg, cancelRequested);
                    case "worker_exited":
                        return cancelRequested
                            ? TradeAttemptResult.Cancelled
                            : TradeAttemptResult.Fail("worker_exited", "O componente de rede parou durante a troca.", retryable: false);
                }
            }
        }
        catch (OperationCanceledException) when (cancelDeadline.IsCancellationRequested)
        {
            // O worker não confirmou a tempo; ele continua encerrando sozinho.
        }
        return TradeAttemptResult.Cancelled;
    }

    public static TradeEvent? ToEvent(JsonObject msg)
    {
        var kindName = msg["kind"]?.GetValue<string>();
        if (kindName is null || !Kinds.TryGetValue(kindName, out var kind))
            return null;
        var data = msg["data"]?.GetValue<string>() is { } b64 ? Convert.FromBase64String(b64) : null;
        return new TradeEvent(kind, DateTimeOffset.UtcNow, msg["message"]?.GetValue<string>())
        {
            TradeIndex = msg["tradeIndex"]?.GetValue<int>(),
            ReceivedData = data,
        };
    }

    public static TradeAttemptResult ToResult(JsonObject end, bool cancelRequested)
    {
        var outcome = end["outcome"]?.GetValue<string>();
        if (outcome == "completed")
            return TradeAttemptResult.Completed;
        if (outcome == "cancelled" || cancelRequested)
            return TradeAttemptResult.Cancelled;
        return TradeAttemptResult.Fail(
            end["code"]?.GetValue<string>() ?? "engine_error",
            end["message"]?.GetValue<string>() ?? "",
            end["retryable"]?.GetValue<bool>() ?? false);
    }

    /// <summary>Tipos de evento do worker (worker/traderock_worker/events.py) → eventos do TradeRock.</summary>
    private static readonly Dictionary<string, TradeEventKind> Kinds = new()
    {
        ["scanning"] = TradeEventKind.Scanning,
        ["beacon_found"] = TradeEventKind.BeaconFound,
        ["beacon_decrypted"] = TradeEventKind.BeaconDecrypted,
        ["join_requested"] = TradeEventKind.JoinRequested,
        ["joined"] = TradeEventKind.Joined,
        ["in_room"] = TradeEventKind.InRoom,
        ["host_sat"] = TradeEventKind.HostSat,
        ["cards_exchanged"] = TradeEventKind.CardsExchanged,
        ["trade_menu_open"] = TradeEventKind.TradeMenuOpen,
        ["trade_offered"] = TradeEventKind.TradeOffered,
        ["confirming"] = TradeEventKind.Confirming,
        ["trade_done"] = TradeEventKind.TradeDone,
        ["trade_cancelled"] = TradeEventKind.TradeCancelled,
        ["leaving"] = TradeEventKind.Leaving,
        ["link_closed"] = TradeEventKind.LinkClosed,
        ["error"] = TradeEventKind.Error,
        ["log"] = TradeEventKind.Log,
    };
}
