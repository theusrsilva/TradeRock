using TradeRock.Core.Pokemon;
using TradeRock.Core.Saves;
using TradeRock.Core.Storage;

namespace TradeRock.App.Api;

public static class PokemonEndpoints
{
    private const long MaxUploadBytes = 64 * 1024; // um .pk3 tem 80 ou 100 bytes

    public sealed record PokemonDto(
        Guid Id, ushort Species, string SpeciesName, string Nickname, byte Level, bool IsShiny, bool IsEgg,
        byte Gender, string OtName, ushort Tid, ushort Sid, string OriginGame, PokemonOrigin Origin,
        string? SourceFileName, int Size, string Sha256, DateTimeOffset AddedAt, ushort HeldItem,
        ushort? EvolvesOnTradeInto, bool Fateful, string? Legality, IReadOnlyList<TradeWarning> Warnings,
        Guid? BoxId, int? Slot, DateTimeOffset? DeletedAt, IReadOnlyList<Guid> TagIds, bool PossibleDuplicate)
    {
        public static PokemonDto From(StoredPokemon p, MyTrainer? me, bool possibleDuplicate = false) => new(
            p.Id, p.Species, p.SpeciesName, p.Nickname, p.Level, p.IsShiny, p.IsEgg, p.Gender, p.OtName,
            p.Tid, p.Sid, p.OriginGame, p.Origin, p.SourceFileName, p.Size, p.Sha256, p.AddedAt, p.HeldItem,
            TradeEvolution.EvolvesInto(p.Species, p.HeldItem), p.Fateful, p.Legality,
            TradeRules.For(new TradeRuleInput(p.Species, p.IsEgg, p.Fateful, p.Level, p.OtName, p.Tid, p.Sid, p.HeldItem), me),
            p.BoxId, p.Slot, p.DeletedAt, p.Tags.Select(t => t.TagId).ToList(), possibleDuplicate);
    }

    public sealed record UploadResult(string FileName, bool Ok, bool Created, PokemonDto? Pokemon, string? Error, string? ErrorMessage);

    public static void MapPokemon(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/pokemon");

        group.MapGet("/", async (PokemonStore store, SettingsStore settings, CancellationToken ct) =>
        {
            var me = await MyTrainerAsync(settings, ct);
            return (await store.ListAsync(ct)).Select(p => PokemonDto.From(p, me));
        });

        group.MapGet("/{id:guid}", async (Guid id, PokemonStore store, SettingsStore settings, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } p ? Results.Ok(PokemonDto.From(p, await MyTrainerAsync(settings, ct))) : Results.NotFound());

        group.MapGet("/{id:guid}/legality", async (Guid id, string? lang, PokemonStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } p
                ? Results.Ok(await store.GetLegalityReportAsync(p, lang is "en" ? "en" : "pt-BR", ct))
                : Results.NotFound());

        // Download (modo navegador). Na janela nativa, a exportação usa /api/vault/export/native.
        group.MapGet("/{id:guid}/file", async (Guid id, string? format, int? size, PokemonStore store, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is not { } p)
                return Results.NotFound();
            format ??= "pk3";
            var bytes = size ?? Pk3Reader.PartySize;
            if (!Pk3Export.Formats.Contains(format) || bytes is not (Pk3Reader.StoredSize or Pk3Reader.PartySize))
                return Results.BadRequest(new { error = "export_invalid" });
            var data = Pk3Export.ToFile(await store.ReadDataAsync(p, ct), format, bytes);
            var name = Pk3Export.FileName(p.Species, string.IsNullOrWhiteSpace(p.Nickname) ? p.SpeciesName : p.Nickname, format);
            return Results.File(data, "application/octet-stream", name);
        });

        // Upload de um ou mais .pk3/.ek3. Protegido contra CSRF pelo cookie SameSite=Strict + filtro de Host;
        // o modo servidor na rede (Entrega 10) acrescenta token anti-CSRF.
        group.MapPost("/", async (HttpRequest request, PokemonStore store, SettingsStore settings, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "expected_form" });
            if (request.ContentLength > MaxUploadBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var form = await request.ReadFormAsync(ct);
            var me = await MyTrainerAsync(settings, ct);
            var results = new List<UploadResult>();
            foreach (var file in form.Files)
            {
                if (file.Length > 1024)
                {
                    results.Add(new UploadResult(file.FileName, false, false, null, "size",
                        "Arquivo grande demais para um Pokémon da Gen 3 (80 ou 100 bytes)."));
                    continue;
                }

                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                try
                {
                    var imported = await store.ImportAsync(buffer.ToArray(), PokemonOrigin.Imported, file.FileName, ct);
                    results.Add(new UploadResult(file.FileName, true, imported.Created, PokemonDto.From(imported.Pokemon, me), null, null));
                }
                catch (InvalidPokemonFileException ex)
                {
                    results.Add(new UploadResult(file.FileName, false, false, null, ex.Code, ex.Message));
                }
            }
            return Results.Ok(results);
        }).DisableAntiforgery();
    }

    private static Task<MyTrainer?> MyTrainerAsync(SettingsStore settings, CancellationToken ct) =>
        settings.GetAsync<MyTrainer?>(BuilderEndpoints.MyTrainerKey, null, ct);
}
