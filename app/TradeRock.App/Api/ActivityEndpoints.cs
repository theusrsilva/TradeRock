using TradeRock.App.Server;
using TradeRock.Core.Activities;
using TradeRock.Core.Games;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Setup;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.App.Api;

/// <summary>Outras atividades além da troca (D26): por enquanto, o Mystery Gift do FRLG e do Sword/Shield.</summary>
public static class ActivityEndpoints
{
    public sealed record GiftDto(string Mode, string? Key = null, string? Data = null, string? Extension = null, string? FileName = null);

    public sealed record StartActivityDto(TradeGame Game, ActivityKind Kind, GiftDto? Gift);

    public sealed record GameActivitiesDto(string Game, IReadOnlyList<ActivityKind> Activities, IReadOnlyList<string> GiftExtensions);

    public sealed record ActivitiesDto(string EngineId, bool EngineSupports, IReadOnlyList<GameActivitiesDto> Games,
        IReadOnlyDictionary<string, GameGifts> Gifts);

    public static void MapActivities(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/activities");

        group.MapGet("/", async (EngineRegistry engines, CancellationToken ct) =>
        {
            var engine = await engines.GetSelectedAsync(ct);
            var games = GiftCatalog.Extensions
                .Select(g => new GameActivitiesDto(GameCatalog.Get(g.Key).Key, [ActivityKind.Gift], g.Value))
                .ToList();
            var gifts = GiftCatalog.Embedded.Games.ToDictionary(g => GameCatalog.Get(g.Key).Key, g => g.Value);
            return Results.Ok(new ActivitiesDto(engine.Id, engine is IActivityEngine, games, gifts));
        });

        group.MapPost("/", async (StartActivityDto body, EngineRegistry engines, SetupService setup, SettingsStore settings,
            ActivityService activities, CancellationToken ct) =>
        {
            var game = GameCatalog.Of(body.Game);
            if (body.Kind != ActivityKind.Gift || body.Gift is null)
                return Invalid("activity_invalid");
            if (!GiftCatalog.Extensions.ContainsKey(game.Family))
                return Invalid("activity_game_unsupported");
            GiftChoice gift;
            try
            {
                gift = new GiftChoice(body.Gift.Mode, body.Gift.Key,
                    body.Gift.Data is null ? null : Convert.FromBase64String(body.Gift.Data), body.Gift.Extension?.TrimStart('.').ToLowerInvariant());
            }
            catch (FormatException)
            {
                return Invalid("gift_file_invalid");
            }
            if (gift.Mode == "file" && GiftCatalog.FileProblem(game.Family, gift.Extension, gift.Data) is { } problem)
                return Invalid(problem);
            if (!GiftCatalog.Embedded.Has(game.Family, gift))
                return Invalid("gift_unknown");

            var engine = await engines.GetSelectedAsync(ct);
            if (engine is not IActivityEngine runner || !runner.SupportsActivity(game.Family, body.Kind))
                return Invalid("activity_engine_unsupported");
            if (!engine.IsSimulation && engine.NeedsSetup)
            {
                var status = await setup.StatusAsync(ct);
                if (status.System.SimulatedScenario is not null)
                    return Invalid("setup_simulated");
                if (!status.ReadyToTrade)
                    return Invalid("setup_incomplete");
            }

            // O treinador que aparece como remetente: o "Meu treinador" do jogo, se houver.
            var me = await settings.GetAsync<MyTrainer?>(UserSettings.Key(BuilderEndpoints.MyTrainerKeyFor(game.Family)), null, ct);
            var trainer = me is null ? TrainerProfile.Default : new TrainerProfile(me.Name, me.Tid, me.Sid);
            var label = gift.Mode switch
            {
                "file" => body.Gift.FileName,
                "event" => GiftCatalog.Embedded.Games[game.Family].Events.First(e => e.Key == gift.Key).Label,
                _ => GiftCatalog.Embedded.Games[game.Family].Presets.First(p => p.Key == gift.Key).Label,
            };
            try
            {
                var snapshot = activities.Start(runner, new ActivityRequest(body.Game, body.Kind, trainer, gift), label);
                return Results.Accepted("/api/activities/current", snapshot);
            }
            catch (RadioBusyException)
            {
                return Results.Conflict(new { error = "busy" });
            }
        });

        group.MapGet("/current", (ActivityService activities) =>
            activities.Current is { } current ? Results.Ok(current) : Results.NoContent());

        group.MapPost("/current/cancel", (ActivityService activities) =>
            activities.Cancel() ? Results.Accepted() : Results.Conflict(new { error = "not_running" }));
    }

    private static IResult Invalid(string code) => Results.BadRequest(new { error = "invalid", errors = new[] { code } });
}
