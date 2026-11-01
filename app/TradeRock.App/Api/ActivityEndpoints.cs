using TradeRock.App.Server;
using TradeRock.Core.Activities;
using TradeRock.Core.Engines.Pokeldn;
using TradeRock.Core.Games;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Setup;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.App.Api;

/// <summary>
/// Outras atividades além da troca (D26): Mystery Gift (FRLG e Sword/Shield), raids (Sword/Shield e Scarlet/Violet) e
/// Union Circle (Scarlet/Violet).
/// </summary>
public static class ActivityEndpoints
{
    public sealed record GiftDto(string Mode, string? Key = null, string? Data = null, string? Extension = null, string? FileName = null);

    /// <summary>A raid escolhida; os Pokémon são do cofre (ids).</summary>
    public sealed record RaidDto(
        string Kind,
        Guid? Boss = null,
        Guid? Offer = null,
        Guid? Pokemon = null,
        string? Event = null,
        string? EventTitle = null,
        string? EventBoss = null,
        bool Gigantamax = false,
        int Move = 1,
        int? TargetSpecies = null,
        int? Legendary = null,
        string? Code = null,
        string? Version = null,
        string? Map = null,
        string? Progress = null,
        string? Content = null,
        string? Seed = null,
        int? Group = null,
        int Players = 1);

    public sealed record CircleDto(string Kind, string Code, string Task = "snacksworth", int? Bp = null,
        int? Species = null, int Level = 30, bool Shiny = false, int? Tera = null);

    public sealed record StartActivityDto(TradeGame Game, ActivityKind Kind, GiftDto? Gift, RaidDto? Raid = null, CircleDto? Circle = null);

    /// <param name="Supported">As atividades desse jogo que a conexão escolhida faz.</param>
    public sealed record GameActivitiesDto(string Game, IReadOnlyList<ActivityKind> Activities, IReadOnlyList<ActivityKind> Supported,
        IReadOnlyList<string> GiftExtensions);

    public sealed record ActivitiesDto(string EngineId, bool EngineSupports, IReadOnlyList<GameActivitiesDto> Games,
        IReadOnlyDictionary<string, GameGifts> Gifts, IReadOnlyList<int> LairLegendaries);

    public static void MapActivities(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/activities");

        group.MapGet("/", async (EngineRegistry engines, CancellationToken ct) =>
        {
            var engine = await engines.GetSelectedAsync(ct);
            var runner = engine as IActivityEngine;
            var games = GameCatalog.All
                .Select(g =>
                {
                    var kinds = Enum.GetValues<ActivityKind>().Where(k => PokeldnActivity.Supports(g.Family, k)).ToList();
                    return new GameActivitiesDto(g.Key, kinds, [.. kinds.Where(k => runner?.SupportsActivity(g.Family, k) == true)],
                        GiftCatalog.Extensions.GetValueOrDefault(g.Family) ?? []);
                })
                .Where(g => g.Activities.Count > 0)
                .ToList();
            var gifts = GiftCatalog.Embedded.Games.ToDictionary(g => GameCatalog.Get(g.Key).Key, g => g.Value);
            return Results.Ok(new ActivitiesDto(engine.Id, runner is not null, games, gifts, RaidKinds.LairLegendaries));
        });

        // As raids de evento embutidas (catalog/raids.json, ~1 MB): a tela lê só quando precisa.
        group.MapGet("/raid-events", () => Results.Bytes(RaidCatalog.Embedded.Json, "application/json"));

        group.MapPost("/", async (StartActivityDto body, EngineRegistry engines, SetupService setup, SettingsStore settings,
            PokemonStore store, ActivityService activities, CancellationToken ct) =>
        {
            var game = GameCatalog.Of(body.Game);
            if (!PokeldnActivity.Supports(game.Family, body.Kind))
                return Invalid("activity_game_unsupported");

            GiftChoice? gift = null;
            RaidChoice? raid = null;
            CircleChoice? circle = null;
            string? label = null;
            if (body.Kind == ActivityKind.Circle)
            {
                if (body.Circle is not { } c)
                    return Invalid("activity_invalid");
                if (c.Code is not { Length: 4 } code || !code.All(char.IsAsciiDigit))
                    return Invalid("circle_code_invalid");
                if (c.Kind == "quests")
                {
                    if (c.Task is not ("snacksworth" or "bp") || (c.Task == "bp" && c.Bp is not (>= 1 and <= CircleChoice.MaxBp)))
                        return Invalid("circle_invalid");
                    circle = new CircleChoice("quests", code, c.Task, c.Task == "bp" ? c.Bp : null);
                }
                else if (c.Kind == "encounters")
                {
                    if (c.Species is not (>= 1 and <= 1025) || c.Species is not { } species || !game.HasSpecies((ushort)species) || c.Level is < 1 or > 100 || c.Tera is < 0 or > 17)
                        return Invalid("circle_invalid");
                    circle = new CircleChoice("encounters", code, Species: species, Level: c.Level, Shiny: c.Shiny, Tera: c.Tera);
                }
                else
                {
                    return Invalid("circle_invalid");
                }
            }
            else if (body.Kind == ActivityKind.Gift)
            {
                if (body.Gift is null)
                    return Invalid("activity_invalid");
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
                label = gift.Mode switch
                {
                    "file" => body.Gift.FileName,
                    "event" => GiftCatalog.Embedded.Games[game.Family].Events.First(e => e.Key == gift.Key).Label,
                    _ => GiftCatalog.Embedded.Games[game.Family].Presets.First(p => p.Key == gift.Key).Label,
                };
            }
            else
            {
                if (body.Raid is null)
                    return Invalid("activity_invalid");
                var (choice, error) = await RaidAsync(body.Raid, game, store, ct);
                if (error is not null)
                    return Invalid(error);
                raid = choice;
                label = body.Raid.EventTitle;
            }

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

            // O treinador do lado do TradeRock: o "Meu treinador" do jogo, se houver.
            var me = await settings.GetAsync<MyTrainer?>(UserSettings.Key(BuilderEndpoints.MyTrainerKeyFor(game.Family)), null, ct);
            var trainer = me is null ? TrainerProfile.Default : new TrainerProfile(me.Name, me.Tid, me.Sid);
            try
            {
                var snapshot = activities.Start(runner, new ActivityRequest(body.Game, body.Kind, trainer, gift, raid, circle), label);
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

    /// <summary>Confere a raid com o catálogo de eventos e lê os Pokémon do cofre (no formato do jogo).</summary>
    private static async Task<(RaidChoice?, string?)> RaidAsync(RaidDto r, SwitchGame game, PokemonStore store, CancellationToken ct)
    {
        if (!RaidKinds.Game.TryGetValue(r.Kind, out var family) || family != game.Family)
            return (null, "raid_invalid");

        async Task<(byte[]?, string?)> Mon(Guid? id, bool required)
        {
            if (id is null)
                return (null, required ? "raid_pokemon_missing" : null);
            if (await store.GetAsync(id.Value, ct) is not { DeletedAt: null } p)
                return (null, "unknown_pokemon");
            return p.Family != game.Family ? (null, "pokemon_wrong_game") : (await store.ReadDataAsync(p, ct), null);
        }

        var choice = new RaidChoice(r.Kind, Event: string.IsNullOrWhiteSpace(r.Event) ? null : r.Event);
        switch (r.Kind)
        {
            case RaidKinds.MaxRaidHost:
            {
                var (offer, e1) = await Mon(r.Offer, true);
                if (e1 is not null)
                    return (null, e1);
                if (choice.Event is null)
                {
                    var (boss, e2) = await Mon(r.Boss, true);
                    return e2 is not null ? (null, e2) : (choice with { Boss = boss, Offer = offer, Gigantamax = r.Gigantamax }, null);
                }
                if (r.EventBoss is null || !RaidCatalog.Embedded.HasSwshBoss(choice.Event, r.EventBoss))
                    return (null, "raid_event_invalid");
                return (choice with { Offer = offer, EventBoss = r.EventBoss }, null);
            }
            case RaidKinds.MaxRaidJoin:
            {
                var (offer, e) = await Mon(r.Offer, false);
                if (e is not null)
                    return (null, e);
                if (r.Move is < 0 or > 4 || (r.TargetSpecies is { } t && (t is < 1 or > ushort.MaxValue || !game.HasSpecies((ushort)t))))
                    return (null, "raid_invalid");
                // Mini IA (D31): os tipos do chefe informado e do Pokémon que o TradeRock leva.
                IReadOnlyList<int>? Types(ushort species, byte form = 0)
                {
                    var info = game.PersonalTable.GetFormEntry(species, form);
                    return info.Type1 == info.Type2 ? [info.Type1] : [info.Type1, info.Type2];
                }
                var mine = r.Offer is { } id && await store.GetAsync(id, ct) is { } stored ? Types(stored.Species, stored.Form) : null;
                return (choice with
                {
                    Offer = offer, Move = r.Move,
                    TargetTypes = r.Move == 0 && r.TargetSpecies is { } boss ? Types((ushort)boss) : null,
                    AttackerTypes = r.Move == 0 ? mine : null,
                }, null);
            }
            case RaidKinds.LairHost:
                if (r.Legendary is not { } legendary || !RaidKinds.LairLegendaries.Contains(legendary))
                    return (null, "raid_invalid");
                if (r.Code is { Length: > 0 } code && !TradeRequest.IsValidLinkCode(LinkCodeKind.Digits8, code))
                    return (null, "link_code_invalid");
                return (choice with { Legendary = legendary, Code = string.IsNullOrEmpty(r.Code) ? null : r.Code }, null);
            case RaidKinds.LairJoin:
                return (choice, null);
            case RaidKinds.TeraRaidHost:
            {
                var (pokemon, e) = await Mon(r.Pokemon, true);
                if (e is not null)
                    return (null, e);
                var version = r.Version ?? "violet";
                var progress = r.Progress ?? "4star";
                if (!RaidKinds.SvVersions.Contains(version) || !RaidKinds.SvProgress.Contains(progress)
                    || (r.Map is not null && !RaidKinds.SvMaps.Contains(r.Map)) || (r.Content is not null && !RaidKinds.SvContent.Contains(r.Content))
                    || r.Players is < 1 or > 3)
                    return (null, "raid_invalid");
                var seed = string.IsNullOrWhiteSpace(r.Seed) ? null : r.Seed.Trim();
                if (seed is not null && (seed.Length != 8 || !seed.All(Uri.IsHexDigit)))
                    return (null, "raid_seed_invalid");
                if (choice.Event is not null && !RaidCatalog.Embedded.HasSv(choice.Event, version, r.Group, progress))
                    return (null, "raid_event_invalid");
                return (choice with
                {
                    Pokemon = pokemon, Version = version, Progress = progress, Seed = seed, Players = r.Players,
                    // Com evento, o mapa e o conteúdo vêm dele (catálogo do pokeldn: unless --raid-event).
                    Map = choice.Event is null ? r.Map : null, Content = choice.Event is null ? r.Content : null,
                    Group = choice.Event is null ? null : r.Group,
                }, null);
            }
            case RaidKinds.TeraRaidJoin:
            {
                var (pokemon, e) = await Mon(r.Pokemon, true);
                return e is not null ? (null, e) : (choice with { Pokemon = pokemon }, null);
            }
            default:
                return (null, "raid_invalid");
        }
    }

    private static IResult Invalid(string code) => Results.BadRequest(new { error = "invalid", errors = new[] { code } });
}
