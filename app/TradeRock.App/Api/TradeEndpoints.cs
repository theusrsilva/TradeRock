using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.App.Api;

public static class TradeEndpoints
{
    public sealed record TrainerDto(string Name, ushort Tid, ushort? Sid);

    public sealed record StartTradeDto(
        IReadOnlyList<Guid> Party,
        IReadOnlyList<int> OfferedSlots,
        TradeGame Game,
        TrainerDto Trainer,
        SimulationOptions? Simulation);

    public sealed record TradeHistoryItemDto(
        Guid Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string EngineId, bool IsSimulation,
        TradeGame Game, string TrainerName, ushort Tid, ushort? Sid, TradeSessionStatus Status, int Attempts,
        int TradesPlanned, int TradesCompleted, string? FailureCode, string? FailureMessage,
        IReadOnlyList<TradeEntryDto> Entries);

    public sealed record TradeEntryDto(int TradeIndex, Guid SentPokemonId, Guid? ReceivedPokemonId);

    public static void MapTrades(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/trades");

        group.MapPost("/", async (StartTradeDto body, EngineRegistry engines, TradeCoordinator coordinator,
            PokemonStore store, SettingsStore settings, CancellationToken ct) =>
        {
            if (body.Party is null || body.OfferedSlots is null || body.Trainer is null)
                return Results.BadRequest(new { error = "invalid", errors = new[] { "missing_fields" } });

            // Todo Pokémon da troca já está guardado no cofre: o time vem de lá, por id.
            var party = new List<PartyMember>();
            foreach (var id in body.Party)
            {
                if (await store.GetAsync(id, ct) is not { } p)
                    return Results.BadRequest(new { error = "invalid", errors = new[] { "unknown_pokemon" } });
                var name = string.IsNullOrWhiteSpace(p.Nickname) ? p.SpeciesName : p.Nickname;
                party.Add(new PartyMember(p.Id, await store.ReadDataAsync(p, ct), name));
            }

            var engine = await engines.GetSelectedAsync(ct);
            var request = new TradeRequest(
                party, body.OfferedSlots, body.Game,
                new TrainerProfile(body.Trainer.Name, body.Trainer.Tid, body.Trainer.Sid),
                engine.IsSimulation ? body.Simulation : null);

            var tradeSettings = await settings.GetAsync(SettingsEndpoints.TradeSettingsKey, TradeSettings.Default, ct);
            try
            {
                var snapshot = coordinator.Start(engine, request, tradeSettings);
                return Results.Accepted("/api/trades/current", snapshot);
            }
            catch (InvalidTradeRequestException ex)
            {
                return Results.BadRequest(new { error = "invalid", errors = ex.Errors });
            }
            catch (TradeBusyException)
            {
                return Results.Conflict(new { error = "busy" });
            }
        });

        group.MapGet("/current", (TradeCoordinator coordinator) =>
            coordinator.Current is { } s ? Results.Ok(s) : Results.NoContent());

        group.MapPost("/current/cancel", async (TradeCoordinator coordinator) =>
        {
            await coordinator.CancelAsync(TimeSpan.FromSeconds(30));
            return coordinator.Current is { } s ? Results.Ok(s) : Results.NoContent();
        });

        group.MapGet("/events", StreamEventsAsync);

        group.MapGet("/", async (TradeRecorder recorder, int? limit, CancellationToken ct) =>
            (await recorder.ListAsync(Math.Clamp(limit ?? 50, 1, 500), ct)).Select(ToDto));

        group.MapGet("/{id:guid}", async (Guid id, TradeRecorder recorder, CancellationToken ct) =>
        {
            if (await recorder.GetAsync(id, ct) is not { } record)
                return Results.NotFound();
            using var events = JsonDocument.Parse(record.EventsJson);
            return Results.Ok(new { trade = ToDto(record), events = events.RootElement.Clone() });
        });
    }

    /// <summary>
    /// Progresso ao vivo por Server-Sent Events. Quem conecta recebe os eventos da sessão atual e
    /// depois os novos. O EventSource do navegador reconecta sozinho mandando Last-Event-ID.
    /// </summary>
    private static async Task StreamEventsAsync(HttpContext context, TradeCoordinator coordinator,
        IOptions<JsonOptions> json, long? after)
    {
        var afterSeq = after ?? 0;
        if (long.TryParse(context.Request.Headers["Last-Event-ID"], out var lastId))
            afterSeq = Math.Max(afterSeq, lastId);

        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.Body.FlushAsync(context.RequestAborted);

        using var subscription = coordinator.Subscribe(afterSeq);
        var ct = context.RequestAborted;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    if (!await subscription.Reader.WaitToReadAsync(heartbeat.Token))
                        break;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await context.Response.WriteAsync(": keep-alive\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                    continue;
                }

                while (subscription.Reader.TryRead(out var e))
                {
                    var data = JsonSerializer.Serialize(e, json.Value.SerializerOptions);
                    await context.Response.WriteAsync($"id: {e.Seq}\nevent: trade\ndata: {data}\n\n", ct);
                }
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // cliente desconectou
        }
    }

    private static TradeHistoryItemDto ToDto(TradeRecord r) => new(
        r.Id, r.StartedAt, r.EndedAt, r.EngineId, r.IsSimulation, r.Game, r.TrainerName, r.Tid, r.Sid, r.Status,
        r.Attempts, r.TradesPlanned, r.TradesCompleted, r.FailureCode, r.FailureMessage,
        r.Entries.OrderBy(e => e.TradeIndex).Select(e => new TradeEntryDto(e.TradeIndex, e.SentPokemonId, e.ReceivedPokemonId)).ToList());
}
