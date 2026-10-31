using Microsoft.EntityFrameworkCore;
using TradeRock.Core.Games;

namespace TradeRock.Core.Storage;

public sealed class VaultException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Filtros do cofre. Campos nulos não filtram.</summary>
public sealed record VaultQuery
{
    /// <summary>"all" (padrão), "inbox", "trash" ou o id de uma caixa.</summary>
    public string? Scope { get; init; }
    public string? Text { get; init; }
    public bool? Shiny { get; init; }
    public string? Legality { get; init; }
    public PokemonOrigin? Origin { get; init; }
    public IReadOnlyList<Guid>? TagIds { get; init; }
    public byte? MinLevel { get; init; }
    public byte? MaxLevel { get; init; }

    /// <summary>Só os Pokémon do formato deste jogo.</summary>
    public GameFamily? Family { get; init; }
}

public sealed record BoxSummary(Guid? Id, string Name, int Order, int Count);

public sealed record VaultOverview(IReadOnlyList<BoxSummary> Boxes, int InboxCount, int TrashCount);

public sealed record EmptyTrashResult(int Deleted, int KeptBecauseOfHistory);

/// <summary>
/// O cofre: caixas de 30 posições (como no jogo), Caixa de entrada para quem chega, tags, lixeira
/// e busca. As regras de posição são garantidas também pelo índice único (BoxId, Slot).
/// </summary>
public sealed class VaultStore(IDbContextFactory<TradeRockDb> dbFactory, PokemonStore pokemon, TimeProvider time)
{
    public const int MaxBoxes = 100;
    public const int MaxNameLength = 24;

    // ---------- Caixas ----------

    public async Task<VaultOverview> OverviewAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var counts = await db.Pokemon.Where(p => p.DeletedAt == null && p.BoxId != null)
            .GroupBy(p => p.BoxId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var boxes = await db.Boxes.AsNoTracking().OrderBy(b => b.Order).ToListAsync(ct);
        var summaries = boxes.Select(b => new BoxSummary(b.Id, b.Name, b.Order,
            counts.FirstOrDefault(c => c.Key == b.Id)?.Count ?? 0)).ToList();
        var inbox = await db.Pokemon.CountAsync(p => p.DeletedAt == null && p.BoxId == null, ct);
        var trash = await db.Pokemon.CountAsync(p => p.DeletedAt != null, ct);
        return new VaultOverview(summaries, inbox, trash);
    }

    public async Task<VaultBox> CreateBoxAsync(string name, CancellationToken ct)
    {
        name = ValidateName(name);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Boxes.CountAsync(ct) >= MaxBoxes)
            throw new VaultException("too_many_boxes");
        var order = await db.Boxes.Select(b => (int?)b.Order).MaxAsync(ct) ?? -1;
        var box = new VaultBox { Id = Guid.CreateVersion7(), Name = name, Order = order + 1, CreatedAt = time.GetUtcNow() };
        db.Boxes.Add(box);
        await db.SaveChangesAsync(ct);
        return box;
    }

    public async Task RenameBoxAsync(Guid id, string name, CancellationToken ct)
    {
        name = ValidateName(name);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var box = await db.Boxes.FindAsync([id], ct) ?? throw new VaultException("box_not_found");
        box.Name = name;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderBoxesAsync(IReadOnlyList<Guid> order, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var boxes = await db.Boxes.ToListAsync(ct);
        if (order.Count != boxes.Count || order.Except(boxes.Select(b => b.Id)).Any())
            throw new VaultException("order_invalid");
        foreach (var box in boxes)
            box.Order = order.ToList().IndexOf(box.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteBoxAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var box = await db.Boxes.FindAsync([id], ct) ?? throw new VaultException("box_not_found");
        if (await db.Pokemon.AnyAsync(p => p.BoxId == id, ct))
            throw new VaultException("box_not_empty");
        db.Boxes.Remove(box);
        await db.SaveChangesAsync(ct);
    }

    private static string ValidateName(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > MaxNameLength)
            throw new VaultException("name_invalid");
        return name;
    }

    // ---------- Posições ----------

    /// <summary>
    /// Move para uma caixa (posição escolhida ou a primeira livre) ou para a Caixa de entrada
    /// (<paramref name="boxId"/> null). Se a posição estiver ocupada, os dois trocam de lugar.
    /// </summary>
    public async Task MoveAsync(Guid pokemonId, Guid? boxId, int? slot, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var moving = await db.Pokemon.SingleOrDefaultAsync(p => p.Id == pokemonId, ct) ?? throw new VaultException("pokemon_not_found");
        if (moving.DeletedAt is not null)
            throw new VaultException("pokemon_in_trash");

        if (boxId is null)
        {
            moving.BoxId = null;
            moving.Slot = null;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (!await db.Boxes.AnyAsync(b => b.Id == boxId, ct))
            throw new VaultException("box_not_found");

        var occupied = await db.Pokemon.Where(p => p.BoxId == boxId && p.DeletedAt == null && p.Id != pokemonId)
            .ToDictionaryAsync(p => p.Slot!.Value, ct);

        int target;
        if (slot is { } s)
        {
            if (s is < 0 or >= VaultBox.Slots)
                throw new VaultException("slot_invalid");
            target = s;
        }
        else
        {
            target = Enumerable.Range(0, VaultBox.Slots).Where(i => !occupied.ContainsKey(i)).DefaultIfEmpty(-1).First();
            if (target < 0)
                throw new VaultException("box_full");
        }

        var (fromBox, fromSlot) = (moving.BoxId, moving.Slot);

        // Libera a posição de origem primeiro (índice único).
        moving.BoxId = null;
        moving.Slot = null;
        await db.SaveChangesAsync(ct);

        if (occupied.TryGetValue(target, out var other))
        {
            other.BoxId = fromBox;
            other.Slot = fromBox is null ? null : fromSlot;
            await db.SaveChangesAsync(ct);
        }

        moving.BoxId = boxId;
        moving.Slot = target;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------- Busca ----------

    public async Task<IReadOnlyList<StoredPokemon>> SearchAsync(VaultQuery q, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<StoredPokemon> query = db.Pokemon.AsNoTracking().Include(p => p.Tags);

        query = (q.Scope ?? "all") switch
        {
            "trash" => query.Where(p => p.DeletedAt != null),
            "inbox" => query.Where(p => p.DeletedAt == null && p.BoxId == null),
            "all" => query.Where(p => p.DeletedAt == null),
            var id when Guid.TryParse(id, out var boxId) => query.Where(p => p.DeletedAt == null && p.BoxId == boxId),
            _ => throw new VaultException("scope_invalid"),
        };
        if (q.Family is { } family)
            query = query.Where(p => p.Family == family);

        if (q.Shiny is { } shiny)
            query = query.Where(p => p.IsShiny == shiny);
        if (!string.IsNullOrEmpty(q.Legality))
            query = q.Legality == "unknown" ? query.Where(p => p.Legality == null) : query.Where(p => p.Legality == q.Legality);
        if (q.Origin is { } origin)
            query = query.Where(p => p.Origin == origin);
        if (q.MinLevel is { } min)
            query = query.Where(p => p.Level >= min);
        if (q.MaxLevel is { } max)
            query = query.Where(p => p.Level <= max);
        var list = await query.ToListAsync(ct);

        if (q.TagIds is { Count: > 0 } tags)
            list = list.Where(p => tags.All(t => p.Tags.Any(pt => pt.TagId == t))).ToList();

        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            // Texto: nome da espécie, apelido, treinador ou número (#25 ou 25). Feito em memória:
            // comparação sem acentos/maiúsculas e o cofre é pequeno.
            var text = q.Text.Trim().TrimStart('#');
            var number = ushort.TryParse(text, out var n) ? n : (ushort?)null;
            list = list.Where(p =>
                    (number is { } num && p.Species == num)
                    || p.SpeciesName.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || p.Nickname.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || p.OtName.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return list
            .OrderBy(p => p.BoxId is null ? 0 : 1)
            .ThenBy(p => p.Slot ?? int.MaxValue)
            .ThenByDescending(p => p.AddedAt)
            .ToList();
    }

    /// <summary>
    /// Ids de "possíveis duplicatas": mesmo Pokémon (espécie, PID e treinador original) guardado
    /// com dados diferentes (por exemplo, depois de subir de nível). Arquivos idênticos já não se repetem.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> PossibleDuplicatesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Pokemon.AsNoTracking().Where(p => p.DeletedAt == null && p.Pid != null)
            .Select(p => new { p.Id, p.Species, p.Pid, p.Tid, p.Sid, p.OtName }).ToListAsync(ct);
        return rows.GroupBy(r => (r.Species, r.Pid, r.Tid, r.Sid, r.OtName))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(r => r.Id))
            .ToHashSet();
    }

    // ---------- Tags ----------

    public static readonly string[] TagColors = ["slate", "red", "amber", "emerald", "sky", "violet", "pink"];

    public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<Tag> SaveTagAsync(Guid? id, string name, string color, CancellationToken ct)
    {
        name = ValidateName(name);
        if (!TagColors.Contains(color))
            throw new VaultException("color_invalid");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Tags.AnyAsync(t => t.Name == name && t.Id != id, ct))
            throw new VaultException("tag_exists");

        Tag tag;
        if (id is { } existing)
        {
            tag = await db.Tags.FindAsync([existing], ct) ?? throw new VaultException("tag_not_found");
            tag.Name = name;
            tag.Color = color;
        }
        else
        {
            tag = new Tag { Id = Guid.CreateVersion7(), Name = name, Color = color };
            db.Tags.Add(tag);
        }
        await db.SaveChangesAsync(ct);
        return tag;
    }

    public async Task DeleteTagAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deleted = await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0)
            throw new VaultException("tag_not_found");
    }

    /// <summary>Adiciona e/ou remove tags de vários Pokémon de uma vez.</summary>
    public async Task SetTagsAsync(IReadOnlyList<Guid> pokemonIds, IReadOnlyList<Guid> add, IReadOnlyList<Guid> remove, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var validTags = await db.Tags.Where(t => add.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
        if (validTags.Count != add.Distinct().Count())
            throw new VaultException("tag_not_found");

        // Só Pokémon deste cofre (PokemonTags não tem filtro de dono próprio).
        var known = await db.Pokemon.Where(p => pokemonIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        await db.PokemonTags.Where(pt => known.Contains(pt.PokemonId) && remove.Contains(pt.TagId)).ExecuteDeleteAsync(ct);

        var existing = await db.PokemonTags.Where(pt => known.Contains(pt.PokemonId)).ToListAsync(ct);
        foreach (var pid in known)
        {
            foreach (var tid in validTags.Where(t => !existing.Any(e => e.PokemonId == pid && e.TagId == t)))
                db.PokemonTags.Add(new PokemonTag { PokemonId = pid, TagId = tid });
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------- Lixeira ----------

    public async Task TrashAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        foreach (var p in await db.Pokemon.Where(p => ids.Contains(p.Id) && p.DeletedAt == null).ToListAsync(ct))
        {
            p.DeletedAt = now;
            p.BoxId = null;
            p.Slot = null;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Volta da lixeira para a Caixa de entrada.</summary>
    public async Task RestoreAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var p in await db.Pokemon.Where(p => ids.Contains(p.Id) && p.DeletedAt != null).ToListAsync(ct))
            p.DeletedAt = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Apaga de vez o que está na lixeira, menos os Pokémon que aparecem no histórico de trocas
    /// (o histórico precisa deles). Os arquivos .pk3 dos apagados saem do disco.
    /// </summary>
    public async Task<EmptyTrashResult> EmptyTrashAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var inHistory = await db.TradeEntries.Select(e => e.SentPokemonId)
            .Union(db.TradeEntries.Where(e => e.ReceivedPokemonId != null).Select(e => e.ReceivedPokemonId!.Value))
            .ToListAsync(ct);
        var trashed = await db.Pokemon.Where(p => p.DeletedAt != null).ToListAsync(ct);
        var deletable = trashed.Where(p => !inHistory.Contains(p.Id)).ToList();

        db.Pokemon.RemoveRange(deletable);
        await db.SaveChangesAsync(ct);

        // O arquivo é compartilhado entre cofres com o mesmo Pokémon: só sai quando ninguém mais o usa.
        var hashes = deletable.Select(p => p.Sha256).Distinct().ToList();
        var stillUsed = await db.Pokemon.IgnoreQueryFilters().Where(p => hashes.Contains(p.Sha256))
            .Select(p => p.Sha256).Distinct().ToListAsync(ct);
        foreach (var p in deletable.Where(p => !stillUsed.Contains(p.Sha256)))
        {
            var path = pokemon.FilePath(p.Sha256, p.Family);
            if (File.Exists(path))
                File.Delete(path);
        }
        return new EmptyTrashResult(deletable.Count, trashed.Count - deletable.Count);
    }
}
