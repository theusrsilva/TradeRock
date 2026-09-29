using System.IO.Compression;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Saves;
using TradeRock.Core.Storage;

namespace TradeRock.App.Api;

public static class VaultEndpoints
{
    public sealed record NameDto(string Name);
    public sealed record OrderDto(IReadOnlyList<Guid> Ids);
    public sealed record MoveDto(Guid? BoxId, int? Slot);
    public sealed record IdsDto(IReadOnlyList<Guid> Ids);
    public sealed record TagDto(string Name, string Color);
    public sealed record AssignTagsDto(IReadOnlyList<Guid> PokemonIds, IReadOnlyList<Guid>? Add, IReadOnlyList<Guid>? Remove);
    public sealed record ExportDto(IReadOnlyList<Guid> Ids, string Format = "pk3", int Size = 100);

    public static void MapVault(this IEndpointRouteBuilder api)
    {
        var vault = api.MapGroup("/vault");

        vault.MapGet("/", (VaultStore store, CancellationToken ct) => Run(() => store.OverviewAsync(ct)));

        vault.MapPost("/boxes", (NameDto body, VaultStore store, CancellationToken ct) => Run(() => store.CreateBoxAsync(body.Name, ct)));
        vault.MapPut("/boxes/order", (OrderDto body, VaultStore store, CancellationToken ct) => Run(() => store.ReorderBoxesAsync(body.Ids, ct)));
        vault.MapPut("/boxes/{id:guid}", (Guid id, NameDto body, VaultStore store, CancellationToken ct) => Run(() => store.RenameBoxAsync(id, body.Name, ct)));
        vault.MapDelete("/boxes/{id:guid}", (Guid id, VaultStore store, CancellationToken ct) => Run(() => store.DeleteBoxAsync(id, ct)));

        vault.MapGet("/pokemon", async (HttpRequest request, VaultStore store, SettingsStore settings, CancellationToken ct) =>
        {
            var q = request.Query;
            var query = new VaultQuery
            {
                Scope = q["scope"].FirstOrDefault(),
                Text = q["text"].FirstOrDefault(),
                Shiny = bool.TryParse(q["shiny"], out var shiny) ? shiny : null,
                Legality = q["legality"].FirstOrDefault(),
                Origin = Enum.TryParse<PokemonOrigin>(q["origin"], true, out var origin) ? origin : null,
                TagIds = q["tags"].FirstOrDefault()?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => Guid.TryParse(t, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList(),
                MinLevel = byte.TryParse(q["minLevel"], out var min) ? min : null,
                MaxLevel = byte.TryParse(q["maxLevel"], out var max) ? max : null,
            };
            var me = await settings.GetAsync<MyTrainer?>(BuilderEndpoints.MyTrainerKey, null, ct);
            return await Run(async () =>
            {
                var duplicates = await store.PossibleDuplicatesAsync(ct);
                var list = (await store.SearchAsync(query, ct))
                    .Select(p => PokemonEndpoints.PokemonDto.From(p, me, duplicates.Contains(p.Id)));
                if (q["warnings"] == "true")
                    list = list.Where(p => p.Warnings.Count > 0);
                if (q["duplicates"] == "true")
                    list = list.Where(p => p.PossibleDuplicate);
                return list.ToList();
            });
        });

        vault.MapPost("/pokemon/{id:guid}/move", (Guid id, MoveDto body, VaultStore store, CancellationToken ct) =>
            Run(() => store.MoveAsync(id, body.BoxId, body.Slot, ct)));

        vault.MapPost("/trash", (IdsDto body, VaultStore store, CancellationToken ct) => Run(() => store.TrashAsync(body.Ids, ct)));
        vault.MapPost("/restore", (IdsDto body, VaultStore store, CancellationToken ct) => Run(() => store.RestoreAsync(body.Ids, ct)));
        vault.MapPost("/trash/empty", (VaultStore store, CancellationToken ct) => Run(() => store.EmptyTrashAsync(ct)));

        vault.MapGet("/tags", (VaultStore store, CancellationToken ct) => Run(() => store.ListTagsAsync(ct)));
        vault.MapPost("/tags", (TagDto body, VaultStore store, CancellationToken ct) => Run(() => store.SaveTagAsync(null, body.Name, body.Color, ct)));
        vault.MapPut("/tags/{id:guid}", (Guid id, TagDto body, VaultStore store, CancellationToken ct) => Run(() => store.SaveTagAsync(id, body.Name, body.Color, ct)));
        vault.MapDelete("/tags/{id:guid}", (Guid id, VaultStore store, CancellationToken ct) => Run(() => store.DeleteTagAsync(id, ct)));
        vault.MapPost("/tags/assign", (AssignTagsDto body, VaultStore store, CancellationToken ct) =>
            Run(() => store.SetTagsAsync(body.PokemonIds, body.Add ?? [], body.Remove ?? [], ct)));

        // Exportação em lote: .zip (modo navegador) ou arquivos gravados numa pasta escolhida (janela nativa).
        vault.MapPost("/export", async (ExportDto body, PokemonStore store, CancellationToken ct) =>
        {
            if (!IsValidExport(body))
                return Results.BadRequest(new { error = "export_invalid" });
            var files = await ExportFilesAsync(body, store, ct);
            using var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, data) in files)
                {
                    var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                    await using var stream = entry.Open();
                    await stream.WriteAsync(data, ct);
                }
            }
            return Results.File(zip.ToArray(), "application/zip", $"traderock-{files.Count}.zip");
        });

        vault.MapPost("/export/native", async (ExportDto body, PokemonStore store, WindowBridge window, CancellationToken ct) =>
        {
            if (!IsValidExport(body))
                return Results.BadRequest(new { error = "export_invalid" });
            if (!window.HasWindow)
                return Results.Conflict(new { error = "no_window" });

            var files = await ExportFilesAsync(body, store, ct);
            var filter = (body.Format.ToUpperInvariant(), new[] { body.Format });
            var written = new List<string>();
            if (files.Count == 1)
            {
                var path = await window.PickSaveFileAsync("Exportar Pokémon", files[0].Name, [filter]);
                if (path is null)
                    return Results.NoContent();
                await File.WriteAllBytesAsync(path, files[0].Data, ct);
                written.Add(path);
            }
            else
            {
                var folder = await window.PickFolderAsync("Escolha a pasta para exportar");
                if (folder is null)
                    return Results.NoContent();
                foreach (var (name, data) in files)
                {
                    var path = UniquePath(Path.Combine(folder, name));
                    await File.WriteAllBytesAsync(path, data, ct);
                    written.Add(path);
                }
            }
            return Results.Ok(new { written });
        });
    }

    private static bool IsValidExport(ExportDto body) =>
        body.Ids is { Count: > 0 and <= 500 } && Pk3Export.Formats.Contains(body.Format) && body.Size is 80 or 100;

    private static async Task<List<(string Name, byte[] Data)>> ExportFilesAsync(ExportDto body, PokemonStore store, CancellationToken ct)
    {
        var files = new List<(string, byte[])>();
        var names = new HashSet<string>();
        foreach (var id in body.Ids.Distinct())
        {
            if (await store.GetAsync(id, ct) is not { } p)
                continue;
            var data = Pk3Export.ToFile(await store.ReadDataAsync(p, ct), body.Format, body.Size);
            var name = Pk3Export.FileName(p.Species, string.IsNullOrWhiteSpace(p.Nickname) ? p.SpeciesName : p.Nickname, body.Format);
            var unique = name;
            for (var i = 2; !names.Add(unique); i++)
                unique = $"{Path.GetFileNameWithoutExtension(name)}-{i}{Path.GetExtension(name)}";
            files.Add((unique, data));
        }
        return files;
    }

    private static string UniquePath(string path)
    {
        var candidate = path;
        for (var i = 2; File.Exists(candidate); i++)
            candidate = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}-{i}{Path.GetExtension(path)}");
        return candidate;
    }

    /// <summary>VaultException vira 400 com o código; o resto segue normal.</summary>
    internal static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (VaultException ex)
        {
            return Results.BadRequest(new { error = ex.Code });
        }
    }

    internal static async Task<IResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return Results.NoContent();
        }
        catch (VaultException ex)
        {
            return Results.BadRequest(new { error = ex.Code });
        }
    }
}
