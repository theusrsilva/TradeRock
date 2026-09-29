using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace TradeRock.Core.Storage;

/// <summary>Configurações simples (chave → JSON) no SQLite.</summary>
public sealed class SettingsStore(IDbContextFactory<TradeRockDb> dbFactory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, T fallback, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, ct);
        if (entry is null)
            return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(entry.Json, Json) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var json = JsonSerializer.Serialize(value, Json);
        var entry = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (entry is null)
            db.Settings.Add(new SettingEntry { Key = key, Json = json });
        else
            entry.Json = json;
        await db.SaveChangesAsync(ct);
    }
}
