using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using TradeRock.App.Server;
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
        SimulationOptions? Simulation,
        bool AcceptIllegal = false);

    public sealed record TradeHistoryItemDto(
        Guid Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string EngineId, bool IsSimulation,
        TradeGame Game, string TrainerName, ushort Tid, ushort? Sid, TradeSessionStatus Status, int Attempts,
        int TradesPlanned, int TradesCompleted, string? FailureCode, string? FailureMessage,
        IReadOnlyList<TradeEntryDto> Entries);

    public sealed record TradeEntryDto(int TradeIndex, Guid SentPokemonId, Guid? ReceivedPokemonId);

    /// <summary>Pedido na fila, como todos veem: nome da conta e posição (0 = trocando agora).</summary>
    public sealed record QueueItemDto(Guid Id, string? Name, bool IsHost, int Position, bool Mine, DateTimeOffset EnqueuedAt);

    public sealed record QueueDto(IReadOnlyList<QueueItemDto> Entries, bool ServerMode);

    /// <summary>Resposta de POST /trades quando o rádio está ocupado: a troca espera na fila e começa sozinha.</summary>
    public sealed record QueuedDto(bool Queued, Guid EntryId, int Position);

    public static void MapTrades(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/trades");

        group.MapPost("/", async (StartTradeDto body, HttpContext context, EngineRegistry engines, TradeQueue queue,
            PokemonStore store, SettingsStore settings, TradeRock.Core.Setup.SetupService setup, CancellationToken ct) =>
        {
            if (body.Party is null || body.OfferedSlots is null || body.Trainer is null)
                return Results.BadRequest(new { error = "invalid", errors = new[] { "missing_fields" } });

            // Todo Pokémon da troca já está guardado no cofre: o time vem de lá, por id.
            var party = new List<PartyMember>();
            var stored = new List<StoredPokemon>();
            foreach (var id in body.Party)
            {
                if (await store.GetAsync(id, ct) is not { DeletedAt: null } p)
                    return Results.BadRequest(new { error = "invalid", errors = new[] { "unknown_pokemon" } });
                var name = string.IsNullOrWhiteSpace(p.Nickname) ? p.SpeciesName : p.Nickname;
                party.Add(new PartyMember(p.Id, await store.ReadDataAsync(p, ct), name));
                stored.Add(p);
            }

            var tradeSettings = await settings.GetAsync(SettingsEndpoints.TradeSettingsKey, TradeSettings.Default, ct);

            // Pokémon ilegal não vai para o Switch, a não ser no modo avançado e com confirmação explícita.
            var offeredIllegal = body.OfferedSlots
                .Where(slot => slot >= 0 && slot < stored.Count && stored[slot].Legality == "illegal")
                .ToList();
            if (offeredIllegal.Count > 0 && !(tradeSettings.AllowIllegal && body.AcceptIllegal))
            {
                var code = tradeSettings.AllowIllegal ? "illegal_needs_confirmation" : "illegal_pokemon";
                return Results.BadRequest(new { error = "invalid", errors = new[] { code } });
            }

            var engine = await engines.GetSelectedAsync(ct);

            // Motor real só com o assistente concluído no essencial (chaves, rádio, teste com o Switch).
            // O modo demonstração continua liberado.
            if (!engine.IsSimulation)
            {
                var status = await setup.StatusAsync(ct);
                // Cenário simulado no assistente (desenvolvimento): os dados não são deste PC.
                if (status.System.SimulatedScenario is not null)
                    return Results.BadRequest(new { error = "invalid", errors = new[] { "setup_simulated" } });
                if (!status.ReadyToTrade)
                    return Results.BadRequest(new { error = "invalid", errors = new[] { "setup_incomplete" } });
            }
            var request = new TradeRequest(
                party, body.OfferedSlots, body.Game,
                new TrainerProfile(body.Trainer.Name, body.Trainer.Tid, body.Trainer.Sid),
                engine.IsSimulation ? body.Simulation : null);

            // Um rádio, uma troca por vez: com o rádio livre começa agora; senão espera a vez na fila.
            var caller = Caller.Of(context);
            try
            {
                var ticket = queue.Enqueue(caller.UserId, caller.Username, engine, request, tradeSettings);
                return ticket.Started is { } snapshot
                    ? Results.Accepted("/api/trades/current", snapshot)
                    : Results.Accepted("/api/trades/queue", new QueuedDto(true, ticket.EntryId, ticket.Position));
            }
            catch (InvalidTradeRequestException ex)
            {
                return Results.BadRequest(new { error = "invalid", errors = ex.Errors });
            }
            catch (AlreadyQueuedException)
            {
                return Results.Conflict(new { error = "busy" });
            }
        });

        // Cada conta só vê as próprias trocas (a fila mostra a vez de todo mundo).
        group.MapGet("/current", (HttpContext context, TradeCoordinator coordinator) =>
            coordinator.Current is { } s && s.OwnerId == Caller.Of(context).UserId ? Results.Ok(s) : Results.NoContent());

        // Cancela a troca em andamento: quem a pediu ou um administrador.
        group.MapPost("/current/cancel", async (HttpContext context, TradeCoordinator coordinator) =>
        {
            var caller = Caller.Of(context);
            if (coordinator.Current is not { } current)
                return Results.NoContent();
            if (current.OwnerId != caller.UserId && !caller.IsAdmin)
                return Results.Json(new { error = "not_yours" }, statusCode: StatusCodes.Status403Forbidden);
            await coordinator.CancelAsync(TimeSpan.FromSeconds(30));
            return coordinator.Current is { } s && s.OwnerId == caller.UserId ? Results.Ok(s) : Results.NoContent();
        });

        group.MapGet("/queue", (HttpContext context, TradeQueue queue, ServerMode server) =>
        {
            var caller = Caller.Of(context);
            return new QueueDto(queue.Snapshot().Select(e => new QueueItemDto(e.Id, e.OwnerName,
                e.OwnerId == TradeRock.Core.Storage.OwnerScope.LocalOwner, e.Position, e.OwnerId == caller.UserId, e.EnqueuedAt)).ToList(),
                server.IsEnabled);
        });

        // Sai da fila: quem pediu ou um administrador (a troca em andamento se cancela em /current/cancel).
        group.MapDelete("/queue/{id:guid}", (Guid id, HttpContext context, TradeQueue queue) =>
        {
            var caller = Caller.Of(context);
            return queue.Leave(id, caller.IsAdmin ? null : caller.UserId) ? Results.NoContent() : Results.NotFound();
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
        var me = Caller.Of(context).UserId;
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
                    if (coordinator.OwnerOf(e.SessionId) != me)
                        continue; // troca de outra conta
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
