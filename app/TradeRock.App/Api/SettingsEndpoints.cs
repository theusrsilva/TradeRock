using Microsoft.EntityFrameworkCore;
using TradeRock.App.Server;
using TradeRock.Core.Games;
using TradeRock.Core.Engines;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.App.Api;

public static class SettingsEndpoints
{
    public const string TradeSettingsKey = "trade.settings";

    /// <summary>Motor e os jogos que ele troca (chaves do catálogo: frlg, sv...).</summary>
    public sealed record EngineDto(string Id, bool IsSimulation, bool Available, string? ReasonCode, bool Selected, IReadOnlyList<string> Games,
        IReadOnlyList<string> PokeldnGames, bool NeedsSetup, bool IgnoresLinkCode);

    public sealed record SelectEngineDto(string Id);

    /// <summary>Treinador salvo, com os números que o jogo mostra (guardados como TID16/SID16).</summary>
    public sealed record TrainerPresetDto(Guid? Id, string Label, string Name, uint Tid, uint? Sid, TradeGame Game)
    {
        public static TrainerPresetDto From(TrainerPreset p)
        {
            var game = GameCatalog.Of(p.Game);
            var (tid, sid) = TrainerIds.Display(game, p.Tid, p.Sid ?? 0);
            return new(p.Id, p.Label, p.Name, tid, p.Sid is null && !game.SixDigitTrainerId ? null : sid, p.Game);
        }
    }

    public static void MapSettings(this IEndpointRouteBuilder api)
    {
        api.MapGet("/settings/trade", async (SettingsStore settings, CancellationToken ct) =>
            await settings.GetAsync(TradeSettingsKey, TradeSettings.Default, ct));

        api.MapPut("/settings/trade", async (TradeSettings body, SettingsStore settings, CancellationToken ct) =>
        {
            var normalized = body.Normalized();
            await settings.SetAsync(TradeSettingsKey, normalized, ct);
            return normalized;
        });

        api.MapGet("/engines", async (EngineRegistry registry, CancellationToken ct) =>
        {
            var selected = await registry.GetSelectedAsync(ct);
            var result = new List<EngineDto>();
            foreach (var engine in registry.All)
            {
                var availability = await engine.CheckAvailabilityAsync(ct);
                result.Add(new EngineDto(engine.Id, engine.IsSimulation, availability.Available,
                    availability.ReasonCode, engine.Id == selected.Id,
                    GameCatalog.All.Where(g => engine.Supports(g.Family)).Select(g => g.Key).ToList(),
                    GameCatalog.All.Where(g => engine.Supports(g.Family) && engine.UsesPokeldn(g.Family)).Select(g => g.Key).ToList(),
                    engine.NeedsSetup, engine.IgnoresLinkCode));
            }
            return result;
        });

        api.MapPut("/engines/selected", async (SelectEngineDto body, EngineRegistry registry, TradeCoordinator coordinator,
            TradeQueue queue, ServerMode server, CancellationToken ct) =>
        {
            if (coordinator.IsBusy || queue.WaitingCount > 0)
                return Results.Conflict(new { error = "busy" });
            var engine = registry.All.FirstOrDefault(e => e.Id == body.Id);
            if (engine is null)
                return Results.NotFound();
            var availability = await engine.CheckAvailabilityAsync(ct);
            if (!availability.Available)
                return Results.Conflict(new { error = "engine_unavailable", reason = availability.ReasonCode });
            // A placa Wi-Fi sai da rede durante a troca: não pode ser a interface do modo servidor.
            if (body.Id == PythonLdnEngine.EngineId && await server.ConflictsWithWifiAsync(ct))
                return Results.Conflict(new { error = "server_mode_on_radio" });
            await registry.SelectAsync(body.Id, ct);
            return Results.NoContent();
        });

        var presets = api.MapGroup("/trainer-presets");

        presets.MapGet("/", async (IDbContextFactory<TradeRockDb> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var list = await db.TrainerPresets.AsNoTracking().OrderBy(p => p.Label).ToListAsync(ct);
            return list.Select(TrainerPresetDto.From);
        });

        presets.MapPost("/", async (TrainerPresetDto body, IDbContextFactory<TradeRockDb> dbFactory, CancellationToken ct) =>
        {
            if (!Enum.IsDefined(body.Game))
                return Results.BadRequest(new { error = "invalid", errors = new[] { "game_invalid" } });
            var game = GameCatalog.Of(body.Game);
            var errors = TrainerName.Validate(body.Name, game).ToList();
            if (string.IsNullOrWhiteSpace(body.Label) || body.Label.Length > 40)
                errors.Add("preset_label_invalid");
            var ids = TrainerIds.FromDisplay(game, body.Tid, body.Sid ?? 0);
            if (ids is null)
                errors.Add("trainer_id_invalid");
            if (errors.Count > 0)
                return Results.BadRequest(new { error = "invalid", errors });

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var preset = new TrainerPreset
            {
                Id = Guid.CreateVersion7(),
                Label = body.Label.Trim(),
                Name = body.Name,
                Tid = ids!.Value.Tid16,
                Sid = body.Sid is null && !game.SixDigitTrainerId ? null : ids.Value.Sid16,
                Game = body.Game,
            };
            db.TrainerPresets.Add(preset);
            await db.SaveChangesAsync(ct);
            return Results.Ok(TrainerPresetDto.From(preset));
        });

        presets.MapDelete("/{id:guid}", async (Guid id, IDbContextFactory<TradeRockDb> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var deleted = await db.TrainerPresets.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }
}
