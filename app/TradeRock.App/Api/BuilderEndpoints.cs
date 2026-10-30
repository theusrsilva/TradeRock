using TradeRock.App.Server;
using Microsoft.Extensions.Caching.Memory;
using PKHeX.Core;
using TradeRock.Core.Games;
using TradeRock.Core.Builder;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;
using TrainerName = TradeRock.Core.Trading.TrainerName;

namespace TradeRock.App.Api;

public static class BuilderEndpoints
{
    /// <summary>"Meu treinador" do FireRed/LeafGreen; os outros jogos usam "trainer.me.&lt;jogo&gt;" (<see cref="MyTrainerKeyFor"/>).</summary>
    public const string MyTrainerKey = "trainer.me";

    public static string MyTrainerKeyFor(GameFamily family) =>
        family == GameFamily.Frlg ? MyTrainerKey : $"{MyTrainerKey}.{GameCatalog.Get(family).Key}";

    /// <summary>
    /// "Meu treinador" de um jogo como a tela mostra: <see cref="Tid"/>/<see cref="Sid"/> com os dígitos do jogo
    /// (5 no FRLG, 6 e 4 nos outros). Insígnias e Pokédex Nacional só valem no FRLG.
    /// </summary>
    public sealed record MyTrainerDto(string Name, uint Tid, uint? Sid, byte Gender = 0, byte Badges = 8, bool HasNationalDex = true);

    public sealed record GenerateDto(PokemonSpec Spec, string? Language);
    public sealed record AdvancedDto(RawPokemon Raw, string? Language);
    public sealed record SaveDto(byte[] Data);
    public sealed record ShowdownDto(string Text, TradeGame Game = TradeGame.FireRed);
    public sealed record SavImportDto(IReadOnlyList<int> Indices);

    public static void MapBuilder(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/builder");

        group.MapGet("/data", () => new
        {
            species = BuilderData.Species,
            natures = BuilderData.Natures,
            moves = BuilderData.Moves,
            items = BuilderData.Items,
            balls = BuilderData.Balls,
            languages = BuilderData.Languages,
            versions = AdvancedEditor.Versions,
        });

        group.MapGet("/species/{species:int}", (int species) =>
            species is < 1 or > BuilderData.MaxSpecies ? Results.NotFound() : Results.Ok(BuilderData.Details((ushort)species)));

        group.MapGet("/species/{species:int}/encounters", (int species, string? game) =>
            species is < 1 or > BuilderData.MaxSpecies
                ? Results.NotFound()
                : Results.Ok(BuilderData.Encounters((ushort)species, ParseGame(game))));

        group.MapGet("/locations", (string? version) =>
        {
            var v = Enum.TryParse<GameVersion>(version, out var parsed) ? parsed : GameVersion.FR;
            return GameInfo.GetLocationList(v, EntityContext.Gen3).Select(l => new Option(l.Value, l.Text));
        });

        group.MapPost("/generate", async (GenerateDto body, CancellationToken ct) =>
            await PokemonBuilder.GenerateAsync(body.Spec, Lang(body.Language), ct));

        group.MapPost("/advanced", (AdvancedDto body) => AdvancedEditor.Build(body.Raw, Lang(body.Language)));

        // Guarda no cofre o resultado do construtor (automático ou avançado).
        group.MapPost("/save", async (SaveDto body, PokemonStore store, SettingsStore settings, CancellationToken ct) =>
        {
            try
            {
                var imported = await store.ImportAsync(body.Data, PokemonOrigin.Created, null, ct);
                var me = await settings.GetAsync<MyTrainer?>(UserSettings.Key(MyTrainerKey), null, ct);
                return Results.Ok(new { created = imported.Created, pokemon = PokemonEndpoints.PokemonDto.From(imported.Pokemon, me) });
            }
            catch (InvalidPokemonFileException ex)
            {
                return Results.BadRequest(new { error = "invalid", errors = new[] { ex.Code } });
            }
        });

        // Abre um Pokémon do cofre no construtor.
        group.MapGet("/from/{id:guid}", async (Guid id, PokemonStore store, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is not { } p)
                return Results.NotFound();
            var pk = new PK3(await store.ReadDataAsync(p, ct));
            return Results.Ok(new { spec = PokemonSpec.FromPk(pk), raw = RawPokemon.FromPk(pk) });
        });

        group.MapPost("/showdown", async (ShowdownDto body, SettingsStore settings, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text) || body.Text.Length > 20_000)
                return Results.BadRequest(new { error = "invalid", errors = new[] { "showdown_empty" } });
            var trainer = await DefaultTrainerAsync(settings, ct);
            return Results.Ok(ShowdownImport.Parse(body.Text, trainer, body.Game));
        });

        group.MapGet("/default-trainer", async (SettingsStore settings, CancellationToken ct) =>
            await DefaultTrainerAsync(settings, ct));

        MapSaveImport(api);
        MapMyTrainer(api);
    }

    private static void MapSaveImport(IEndpointRouteBuilder api)
    {
        // 1) Envia o .sav: o servidor lê e guarda a lista por 10 minutos.
        api.MapPost("/pokemon/sav", async (HttpRequest request, IMemoryCache cache, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "expected_form" });
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count != 1 || form.Files[0].Length > SaveImport.MaxSaveBytes)
                return Results.BadRequest(new { error = "invalid", errors = new[] { "sav_size" } });

            using var buffer = new MemoryStream();
            await form.Files[0].CopyToAsync(buffer, ct);
            try
            {
                var contents = SaveImport.Read(buffer.ToArray());
                var token = Guid.NewGuid();
                cache.Set($"sav:{token}", contents, TimeSpan.FromMinutes(10));
                return Results.Ok(new
                {
                    token,
                    contents.Game,
                    contents.TrainerName,
                    contents.Tid,
                    contents.Sid,
                    entries = contents.Entries.Select(e => new { e.Index, e.Place, e.Box, e.Slot, e.Summary, e.Legality }),
                });
            }
            catch (InvalidSaveException ex)
            {
                return Results.BadRequest(new { error = "invalid", errors = new[] { $"sav_{ex.Code}" } });
            }
        }).DisableAntiforgery();

        // 2) Importa os escolhidos para o cofre.
        api.MapPost("/pokemon/sav/{token:guid}/import", async (Guid token, SavImportDto body, IMemoryCache cache,
            PokemonStore store, SettingsStore settings, CancellationToken ct) =>
        {
            if (!cache.TryGetValue<SaveContents>($"sav:{token}", out var contents) || contents is null)
                return Results.NotFound(new { error = "sav_expired" });

            var me = await settings.GetAsync<MyTrainer?>(UserSettings.Key(MyTrainerKey), null, ct);
            var results = new List<object>();
            foreach (var index in body.Indices.Distinct())
            {
                var entry = contents.Entries.FirstOrDefault(e => e.Index == index);
                if (entry is null)
                    continue;
                var imported = await store.ImportAsync(entry.Data, PokemonOrigin.Imported, $"{contents.Game}-{entry.Place}-{entry.Box}-{entry.Slot}.pk3", ct);
                results.Add(new { index, created = imported.Created, pokemon = PokemonEndpoints.PokemonDto.From(imported.Pokemon, me) });
            }
            return Results.Ok(results);
        });
    }

    private static void MapMyTrainer(IEndpointRouteBuilder api)
    {
        // ?game=frlg|lgpe|swsh|bdsp|pla|sv|za (padrão frlg). Guardado com TID16/SID16, como no arquivo.
        api.MapGet("/settings/my-trainer", async (string? game, SettingsStore settings, CancellationToken ct) =>
        {
            if (GameParam(game) is not { } g)
                return Results.BadRequest(new { error = "invalid", errors = new[] { "game_invalid" } });
            return await settings.GetAsync<MyTrainer?>(UserSettings.Key(MyTrainerKeyFor(g.Family)), null, ct) is { } me
                ? Results.Ok(ToDto(g, me))
                : Results.NoContent();
        });

        api.MapPut("/settings/my-trainer", async (string? game, MyTrainerDto body, SettingsStore settings, CancellationToken ct) =>
        {
            if (GameParam(game) is not { } g)
                return Results.BadRequest(new { error = "invalid", errors = new[] { "game_invalid" } });
            var errors = TrainerName.Validate(body.Name, g).ToList();
            if (body.Badges > 8)
                errors.Add("badges_invalid");
            var ids = TrainerIds.FromDisplay(g, body.Tid, body.Sid ?? 0);
            if (ids is null)
                errors.Add("trainer_id_invalid");
            if (errors.Count > 0)
                return Results.BadRequest(new { error = "invalid", errors });
            // Da Gen 7 em diante o SID16 faz parte do TID que o jogo mostra: guarda sempre.
            var me = new MyTrainer(body.Name, ids!.Value.Tid16, body.Sid is null && !g.SixDigitTrainerId ? null : ids.Value.Sid16,
                body.Gender, body.Badges, body.HasNationalDex);
            await settings.SetAsync(UserSettings.Key(MyTrainerKeyFor(g.Family)), me, ct);
            return Results.Ok(ToDto(g, me));
        });

        // Um Pokémon capturado no Switch e recebido pelo TradeRock traz o TID e o SID do jogador (no jogo dele).
        api.MapPost("/settings/my-trainer/from-pokemon/{id:guid}", async (Guid id, PokemonStore store, SettingsStore settings, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is not { } p)
                return Results.NotFound();
            var key = UserSettings.Key(MyTrainerKeyFor(p.Family));
            var current = await settings.GetAsync<MyTrainer?>(key, null, ct);
            var me = new MyTrainer(p.OtName, p.Tid, p.Sid, current?.Gender ?? 0, current?.Badges ?? 8, current?.HasNationalDex ?? true);
            await settings.SetAsync(key, me, ct);
            return Results.Ok(ToDto(GameCatalog.Get(p.Family), me));
        });
    }

    private static SwitchGame? GameParam(string? game) => string.IsNullOrEmpty(game) ? GameCatalog.Get(GameFamily.Frlg) : GameCatalog.ByKey(game);

    private static MyTrainerDto ToDto(SwitchGame game, MyTrainer me)
    {
        var (tid, sid) = TrainerIds.Display(game, me.Tid, me.Sid ?? 0);
        return new MyTrainerDto(me.Name, tid, me.Sid is null && !game.SixDigitTrainerId ? null : sid, me.Gender, me.Badges, me.HasNationalDex);
    }

    /// <summary>OT padrão do construtor: "Meu treinador" ou, sem ele, o treinador padrão do motor (EMU).</summary>
    public static async Task<TrainerSpec> DefaultTrainerAsync(SettingsStore settings, CancellationToken ct)
    {
        var me = await settings.GetAsync<MyTrainer?>(UserSettings.Key(MyTrainerKey), null, ct);
        if (me is not null)
            return new TrainerSpec(me.Name, me.Tid, me.Sid ?? 0, me.Gender);
        var d = TrainerProfile.Default;
        return new TrainerSpec(d.Name, d.Tid, d.Sid ?? 0);
    }

    /// <summary>Query string não passa pelo conversor JSON: aceita "fire_red"/"leaf_green" (e os nomes do enum).</summary>
    private static TradeGame ParseGame(string? game) => game?.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "leafgreen" => TradeGame.LeafGreen,
        _ => TradeGame.FireRed,
    };

    private static string Lang(string? language) => language is "en" ? "en" : "pt-BR";
}
