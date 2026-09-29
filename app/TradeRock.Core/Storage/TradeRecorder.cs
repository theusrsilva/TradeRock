using Microsoft.EntityFrameworkCore;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Storage;

/// <summary>Grava os recebidos no cofre e o histórico no SQLite.</summary>
public sealed class TradeRecorder(PokemonStore pokemon, IDbContextFactory<TradeRockDb> dbFactory) : ITradeRecorder
{
    public async Task<Guid> StoreReceivedAsync(byte[] data, CancellationToken ct)
    {
        var result = await pokemon.ImportAsync(data, PokemonOrigin.Received, sourceFileName: null, ct);
        return result.Pokemon.Id;
    }

    public async Task SaveAsync(TradeRecord record, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Trades.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TradeRecord>> ListAsync(int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Trades.AsNoTracking().Include(t => t.Entries)
            .OrderByDescending(t => t.StartedAt).Take(limit).ToListAsync(ct);
    }

    public async Task<TradeRecord?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Trades.AsNoTracking().Include(t => t.Entries).SingleOrDefaultAsync(t => t.Id == id, ct);
    }
}
