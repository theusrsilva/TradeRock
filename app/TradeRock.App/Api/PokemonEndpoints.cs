using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;

namespace TradeRock.App.Api;

public static class PokemonEndpoints
{
    private const long MaxUploadBytes = 64 * 1024; // um .pk3 tem 80 ou 100 bytes

    public sealed record PokemonDto(
        Guid Id, ushort Species, string SpeciesName, string Nickname, byte Level, bool IsShiny, bool IsEgg,
        byte Gender, string OtName, ushort Tid, ushort Sid, string OriginGame, PokemonOrigin Origin,
        string? SourceFileName, int Size, string Sha256, DateTimeOffset AddedAt, ushort HeldItem,
        ushort? EvolvesOnTradeInto)
    {
        public static PokemonDto From(StoredPokemon p) => new(
            p.Id, p.Species, p.SpeciesName, p.Nickname, p.Level, p.IsShiny, p.IsEgg, p.Gender, p.OtName,
            p.Tid, p.Sid, p.OriginGame, p.Origin, p.SourceFileName, p.Size, p.Sha256, p.AddedAt, p.HeldItem,
            TradeEvolution.EvolvesInto(p.Species, p.HeldItem));
    }

    public sealed record UploadResult(string FileName, bool Ok, bool Created, PokemonDto? Pokemon, string? Error, string? ErrorMessage);

    public static void MapPokemon(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/pokemon");

        group.MapGet("/", async (PokemonStore store, CancellationToken ct) =>
            (await store.ListAsync(ct)).Select(PokemonDto.From));

        group.MapGet("/{id:guid}", async (Guid id, PokemonStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } p ? Results.Ok(PokemonDto.From(p)) : Results.NotFound());

        group.MapGet("/{id:guid}/file", async (Guid id, PokemonStore store, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is not { } p)
                return Results.NotFound();
            var data = await store.ReadDataAsync(p, ct);
            var name = $"{p.Species:000}-{Sanitize(p.Nickname)}.pk3";
            return Results.File(data, "application/octet-stream", name);
        });

        // Upload de um ou mais .pk3/.ek3. Protegido contra CSRF pelo cookie SameSite=Strict + filtro de Host;
        // o modo servidor na rede (Entrega 10) acrescenta token anti-CSRF.
        group.MapPost("/", async (HttpRequest request, PokemonStore store, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "expected_form" });
            if (request.ContentLength > MaxUploadBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var form = await request.ReadFormAsync(ct);
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
                    results.Add(new UploadResult(file.FileName, true, imported.Created, PokemonDto.From(imported.Pokemon), null, null));
                }
                catch (InvalidPokemonFileException ex)
                {
                    results.Add(new UploadResult(file.FileName, false, false, null, ex.Code, ex.Message));
                }
            }
            return Results.Ok(results);
        }).DisableAntiforgery();
    }

    private static string Sanitize(string name)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length > 0 ? clean : "pokemon";
    }
}
