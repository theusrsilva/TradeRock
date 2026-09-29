using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Pokemon;

namespace TradeRock.Core.Storage;

public sealed record ImportResult(StoredPokemon Pokemon, bool Created, bool Restored = false);

/// <summary>
/// Guarda Pokémon: o .pk3 decriptado vai para vault/&lt;hh&gt;/&lt;sha256&gt;.pk3 e o resumo para o SQLite.
/// Arquivos idênticos (mesmo hash, inclusive .pk3 vs .ek3 do mesmo Pokémon) viram um só registro.
/// </summary>
public sealed class PokemonStore(IDbContextFactory<TradeRockDb> dbFactory, AppPaths paths, TimeProvider time)
{
    public async Task<ImportResult> ImportAsync(
        ReadOnlyMemory<byte> file, PokemonOrigin origin, string? sourceFileName, CancellationToken ct)
    {
        var pk = Pk3Reader.Read(file.Span);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Pokemon.SingleOrDefaultAsync(p => p.Sha256 == pk.Sha256, ct);
        if (existing is not null)
        {
            // O arquivo pode ter sumido do disco; regrava a partir dos bytes recebidos.
            await WriteFileAsync(pk, ct);
            if (existing.DeletedAt is null)
                return new ImportResult(existing, Created: false);

            // Estava na lixeira: volta para a Caixa de entrada.
            existing.DeletedAt = null;
            await db.SaveChangesAsync(ct);
            return new ImportResult(existing, Created: false, Restored: true);
        }

        await WriteFileAsync(pk, ct);

        var s = pk.Summary;
        var stored = new StoredPokemon
        {
            Id = Guid.CreateVersion7(),
            Sha256 = pk.Sha256,
            Size = pk.Data.Length,
            Species = s.Species,
            SpeciesName = s.SpeciesName,
            Nickname = s.Nickname,
            Level = s.Level,
            IsShiny = s.IsShiny,
            IsEgg = s.IsEgg,
            Gender = s.Gender,
            OtName = s.OtName,
            Tid = s.Tid,
            Sid = s.Sid,
            OriginGame = s.OriginGame,
            HeldItem = s.HeldItem,
            Fateful = s.Fateful,
            Pid = new PK3(pk.Data.ToArray()).PID,
            Legality = AnalyzeLegality(pk.Data),
            LegalityPkhexVersion = LegalityReporter.PkhexVersion,
            Origin = origin,
            SourceFileName = sourceFileName is null ? null : Path.GetFileName(sourceFileName),
            AddedAt = time.GetUtcNow(),
        };
        db.Pokemon.Add(stored);

        try
        {
            await db.SaveChangesAsync(ct);
            return new ImportResult(stored, Created: true);
        }
        catch (DbUpdateException)
        {
            // Duas importações simultâneas do mesmo arquivo: vale a que chegou primeiro.
            await using var retry = await dbFactory.CreateDbContextAsync(ct);
            var winner = await retry.Pokemon.SingleOrDefaultAsync(p => p.Sha256 == pk.Sha256, ct);
            if (winner is null)
                throw;
            return new ImportResult(winner, Created: false);
        }
    }

    public async Task<IReadOnlyList<StoredPokemon>> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Pokemon.AsNoTracking().Include(p => p.Tags).Where(p => p.DeletedAt == null)
            .OrderByDescending(p => p.AddedAt).ToListAsync(ct);
    }

    public async Task<StoredPokemon?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Pokemon.AsNoTracking().Include(p => p.Tags).SingleOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>Lê o .pk3 do disco e confere o hash (arquivo adulterado ou corrompido = erro).</summary>
    public async Task<byte[]> ReadDataAsync(StoredPokemon pokemon, CancellationToken ct)
    {
        var data = await File.ReadAllBytesAsync(FilePath(pokemon.Sha256), ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        if (hash != pokemon.Sha256)
            throw new InvalidDataException($"O arquivo do Pokémon {pokemon.Id} não confere com o hash guardado.");
        return data;
    }

    /// <summary>Relatório de legalidade completo de um Pokémon guardado.</summary>
    public async Task<LegalityReport> GetLegalityReportAsync(StoredPokemon pokemon, string language, CancellationToken ct)
    {
        var data = await ReadDataAsync(pokemon, ct);
        return LegalityReporter.Analyze(new PK3(data), language);
    }

    /// <summary>
    /// Refaz a análise dos Pokémon sem resultado ou analisados por outra versão do PKHeX.
    /// Chamado na inicialização, em segundo plano.
    /// </summary>
    public async Task<int> RefreshLegalityAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var version = LegalityReporter.PkhexVersion;
        var stale = await db.Pokemon.Where(p => p.Legality == null || p.LegalityPkhexVersion != version || p.Pid == null).ToListAsync(ct);
        foreach (var p in stale)
        {
            try
            {
                var data = await ReadDataAsync(p, ct);
                p.Legality = AnalyzeLegality(data);
                p.Fateful = Pk3Reader.Read(data).Summary.Fateful;
                p.Pid = new PK3(data.ToArray()).PID;
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                p.Legality = null; // arquivo sumido ou adulterado: fica sem análise
                continue;
            }
            p.LegalityPkhexVersion = version;
        }
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    public static string AnalyzeLegality(byte[] data) =>
        LegalityReporter.Analyze(new PK3(data.ToArray())).Status.ToString().ToLowerInvariant();

    public string FilePath(string sha256) => Path.Combine(paths.VaultDir, sha256[..2], sha256 + ".pk3");

    private async Task WriteFileAsync(Pk3File pk, CancellationToken ct)
    {
        var target = FilePath(pk.Sha256);
        if (File.Exists(target))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temp, pk.Data, ct);
        File.Move(temp, target, overwrite: true);
    }
}
