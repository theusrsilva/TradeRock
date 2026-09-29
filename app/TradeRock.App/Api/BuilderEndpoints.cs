using Microsoft.Extensions.Caching.Memory;
using PKHeX.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;
using TrainerName = TradeRock.Core.Trading.TrainerName;

namespace TradeRock.App.Api;

public static class BuilderEndpoints
{
    public const string MyTrainerKey = "trainer.me";

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
                var me = await settings.GetAsync<MyTrainer?>(MyTrainerKey, null, ct);
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

            var me = await settings.GetAsync<MyTrainer?>(MyTrainerKey, null, ct);
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
        api.MapGet("/settings/my-trainer", async (SettingsStore settings, CancellationToken ct) =>
            await settings.GetAsync<MyTrainer?>(MyTrainerKey, null, ct) is { } me ? Results.Ok(me) : Results.NoContent());

        api.MapPut("/settings/my-trainer", async (MyTrainer body, SettingsStore settings, CancellationToken ct) =>
        {
            var errors = TrainerName.Validate(body.Name).ToList();
            if (body.Badges > 8)
                errors.Add("badges_invalid");
            if (errors.Count > 0)
                return Results.BadRequest(new { error = "invalid", errors });
            await settings.SetAsync(MyTrainerKey, body, ct);
            return Results.Ok(body);
        });

        // Um Pokémon capturado no Switch e recebido pelo TradeRock traz o TID e o SID do jogador.
        api.MapPost("/settings/my-trainer/from-pokemon/{id:guid}", async (Guid id, PokemonStore store, SettingsStore settings, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is not { } p)
                return Results.NotFound();
            var current = await settings.GetAsync<MyTrainer?>(MyTrainerKey, null, ct);
            var me = new MyTrainer(p.OtName, p.Tid, p.Sid, current?.Gender ?? 0, current?.Badges ?? 8, current?.HasNationalDex ?? true);
            await settings.SetAsync(MyTrainerKey, me, ct);
            return Results.Ok(me);
        });
    }

    /// <summary>OT padrão do construtor: "Meu treinador" ou, sem ele, o treinador padrão do motor (EMU).</summary>
    public static async Task<TrainerSpec> DefaultTrainerAsync(SettingsStore settings, CancellationToken ct)
    {
        var me = await settings.GetAsync<MyTrainer?>(MyTrainerKey, null, ct);
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
