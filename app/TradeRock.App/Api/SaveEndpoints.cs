using Microsoft.Extensions.Caching.Memory;
using TradeRock.Core.Builder;
using TradeRock.Core.Saves;
using TradeRock.Core.Storage;

namespace TradeRock.App.Api;

/// <summary>
/// Injeção no .sav de GBA. Com a janela nativa, o arquivo é escolhido pelo sistema e gravado no
/// mesmo lugar (com backup antes). No navegador (--no-window), o save é enviado e o resultado baixado.
/// </summary>
public static class SaveEndpoints
{
    private static readonly (string Name, string[] Extensions)[] SaveFilters =
        [("Save de GBA", ["sav", "srm", "fla", "dsv"]), ("Todos os arquivos", ["*"])];

    public sealed class SaveSession
    {
        public required Guid Token { get; init; }
        public required string FileName { get; init; }
        public string? Path { get; init; }
        public required byte[] Original { get; set; }
        public byte[]? Result { get; set; }
    }

    public sealed record InjectItemDto(Guid PokemonId, int Box, int Slot);
    public sealed record InjectDto(IReadOnlyList<InjectItemDto> Placements);

    public static void MapSaves(this IEndpointRouteBuilder api)
    {
        var saves = api.MapGroup("/saves");

        // Janela nativa: escolher o arquivo no sistema.
        saves.MapPost("/open", async (WindowBridge window, IMemoryCache cache) =>
        {
            if (!window.HasWindow)
                return Results.Conflict(new { error = "no_window" });
            var path = await window.PickOpenFileAsync("Escolha o save de GBA", SaveFilters);
            if (path is null)
                return Results.NoContent();
            var bytes = await File.ReadAllBytesAsync(path);
            return Describe(cache, new SaveSession { Token = Guid.NewGuid(), FileName = Path.GetFileName(path), Path = path, Original = bytes });
        });

        // Navegador: enviar o arquivo.
        saves.MapPost("/upload", async (HttpRequest request, IMemoryCache cache, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "expected_form" });
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count != 1 || form.Files[0].Length > SaveImport.MaxSaveBytes)
                return Results.BadRequest(new { error = "sav_size" });
            using var buffer = new MemoryStream();
            await form.Files[0].CopyToAsync(buffer, ct);
            return Describe(cache, new SaveSession { Token = Guid.NewGuid(), FileName = Path.GetFileName(form.Files[0].FileName), Original = buffer.ToArray() });
        }).DisableAntiforgery();

        saves.MapGet("/{token:guid}", (Guid token, IMemoryCache cache) =>
            Find(cache, token) is { } s ? Results.Ok(Layout(s)) : Results.NotFound(new { error = "sav_expired" }));

        saves.MapPost("/{token:guid}/inject", async (Guid token, InjectDto body, IMemoryCache cache, PokemonStore store,
            SaveBackups backups, CancellationToken ct) =>
        {
            if (Find(cache, token) is not { } session)
                return Results.NotFound(new { error = "sav_expired" });

            var placements = new List<Placement>();
            foreach (var item in body.Placements ?? [])
            {
                if (await store.GetAsync(item.PokemonId, ct) is not { DeletedAt: null } p)
                    return Results.BadRequest(new { error = "unknown_pokemon" });
                placements.Add(new Placement(await store.ReadDataAsync(p, ct), item.Box, item.Slot));
            }

            // Arquivo no disco mudou desde que foi aberto (o emulador salvou, por exemplo)? Não sobrescrever.
            if (session.Path is { } path && SaveInjection.Sha256(await File.ReadAllBytesAsync(path, ct)) != SaveInjection.Sha256(session.Original))
                return Results.Conflict(new { error = "save_changed" });

            byte[] result;
            try
            {
                result = SaveInjection.Inject(session.Original, placements);
            }
            catch (SaveInjectionException ex)
            {
                return Results.BadRequest(new { error = ex.Code, message = ex.Message });
            }

            var backup = backups.Create(session.Original, session.FileName);
            string? written = null;
            if (session.Path is { } target)
            {
                var temp = target + ".traderock.tmp";
                await File.WriteAllBytesAsync(temp, result, ct);
                File.Move(temp, target, overwrite: true);
                written = target;
            }

            // A sessão passa a apontar para o save novo (dá para injetar mais em seguida).
            session.Original = result;
            session.Result = result;
            return Results.Ok(new { backup, written, download = session.Path is null, save = Layout(session) });
        });

        saves.MapGet("/{token:guid}/download", (Guid token, IMemoryCache cache) =>
            Find(cache, token) is { Result: { } result } s
                ? Results.File(result, "application/octet-stream", s.FileName)
                : Results.NotFound(new { error = "sav_expired" }));
    }

    private static IResult Describe(IMemoryCache cache, SaveSession session)
    {
        try
        {
            var layout = Layout(session);
            cache.Set(Key(session.Token), session, TimeSpan.FromMinutes(30));
            return Results.Ok(layout);
        }
        catch (SaveInjectionException ex)
        {
            return Results.BadRequest(new { error = ex.Code });
        }
    }

    private static object Layout(SaveSession s)
    {
        var layout = SaveInjection.Describe(s.Original);
        return new { token = s.Token, fileName = s.FileName, inPlace = s.Path is not null, layout };
    }

    private static SaveSession? Find(IMemoryCache cache, Guid token) => cache.TryGetValue<SaveSession>(Key(token), out var s) ? s : null;

    private static string Key(Guid token) => $"save-session:{token}";
}
