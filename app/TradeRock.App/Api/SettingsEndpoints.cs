using Microsoft.EntityFrameworkCore;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.App.Api;

public static class SettingsEndpoints
{
    public const string TradeSettingsKey = "trade.settings";

    public sealed record EngineDto(string Id, bool IsSimulation, bool Available, string? ReasonCode, bool Selected);

    public sealed record SelectEngineDto(string Id);

    public sealed record TrainerPresetDto(Guid? Id, string Label, string Name, ushort Tid, ushort? Sid, TradeGame Game);

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
                    availability.ReasonCode, engine.Id == selected.Id));
            }
            return result;
        });

        api.MapPut("/engines/selected", async (SelectEngineDto body, EngineRegistry registry, TradeCoordinator coordinator,
            CancellationToken ct) =>
        {
            if (coordinator.IsBusy)
                return Results.Conflict(new { error = "busy" });
            var engine = registry.All.FirstOrDefault(e => e.Id == body.Id);
            if (engine is null)
                return Results.NotFound();
            var availability = await engine.CheckAvailabilityAsync(ct);
            if (!availability.Available)
                return Results.Conflict(new { error = "engine_unavailable", reason = availability.ReasonCode });
            await registry.SelectAsync(body.Id, ct);
            return Results.NoContent();
        });

        var presets = api.MapGroup("/trainer-presets");

        presets.MapGet("/", async (IDbContextFactory<TradeRockDb> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var list = await db.TrainerPresets.AsNoTracking().OrderBy(p => p.Label).ToListAsync(ct);
            return list.Select(p => new TrainerPresetDto(p.Id, p.Label, p.Name, p.Tid, p.Sid, p.Game));
        });

        presets.MapPost("/", async (TrainerPresetDto body, IDbContextFactory<TradeRockDb> dbFactory, CancellationToken ct) =>
        {
            var errors = TrainerName.Validate(body.Name).ToList();
            if (string.IsNullOrWhiteSpace(body.Label) || body.Label.Length > 40)
                errors.Add("preset_label_invalid");
            if (errors.Count > 0)
                return Results.BadRequest(new { error = "invalid", errors });

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var preset = new TrainerPreset
            {
                Id = Guid.CreateVersion7(),
                Label = body.Label.Trim(),
                Name = body.Name,
                Tid = body.Tid,
                Sid = body.Sid,
                Game = body.Game,
            };
            db.TrainerPresets.Add(preset);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new TrainerPresetDto(preset.Id, preset.Label, preset.Name, preset.Tid, preset.Sid, preset.Game));
        });

        presets.MapDelete("/{id:guid}", async (Guid id, IDbContextFactory<TradeRockDb> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var deleted = await db.TrainerPresets.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }
}
