using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Pokemon;

namespace TradeRock.Core.Storage;

public sealed record ImportResult(StoredPokemon Pokemon, bool Created, bool Restored = false);

/// <summary>
/// Guarda Pokémon: o .pk3 decriptado vai para vault/&lt;hh&gt;/&lt;sha256&gt;.pk3 e o resumo para o SQLite.
/// Arquivos idênticos (mesmo hash, inclusive .pk3 vs .ek3 do mesmo Pokémon) viram um só registro por cofre;
/// o arquivo em disco é um só, mesmo que esteja em vários cofres.
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

    /// <summary>
    /// Troca os dados de um Pokémon do cofre por outros (edição no painel do cofre). O registro continua o
    /// mesmo: id, caixa, posição, etiquetas, origem e as trocas do histórico que apontam para ele.
    /// </summary>
    public async Task<StoredPokemon> ReplaceAsync(Guid id, ReadOnlyMemory<byte> file, CancellationToken ct)
    {
        var pk = Pk3Reader.Read(file.Span);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stored = await db.Pokemon.Include(p => p.Tags).SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new VaultException("pokemon_not_found");
        if (stored.DeletedAt is not null)
            throw new VaultException("pokemon_in_trash");
        if (stored.Sha256 == pk.Sha256)
            return stored;
        // O resultado já existe como outro Pokémon deste cofre (mesmo arquivo): não dá para ter dois iguais.
        if (await db.Pokemon.AnyAsync(p => p.Sha256 == pk.Sha256 && p.Id != id, ct))
            throw new VaultException("pokemon_duplicate");

        await WriteFileAsync(pk, ct);
        var oldSha = stored.Sha256;
        var s = pk.Summary;
        stored.Sha256 = pk.Sha256;
        stored.Size = pk.Data.Length;
        stored.Species = s.Species;
        stored.SpeciesName = s.SpeciesName;
        stored.Nickname = s.Nickname;
        stored.Level = s.Level;
        stored.IsShiny = s.IsShiny;
        stored.IsEgg = s.IsEgg;
        stored.Gender = s.Gender;
        stored.OtName = s.OtName;
        stored.Tid = s.Tid;
        stored.Sid = s.Sid;
        stored.OriginGame = s.OriginGame;
        stored.HeldItem = s.HeldItem;
        stored.Fateful = s.Fateful;
        stored.Pid = new PK3(pk.Data.ToArray()).PID;
        stored.Legality = AnalyzeLegality(pk.Data);
        stored.LegalityPkhexVersion = LegalityReporter.PkhexVersion;
        await db.SaveChangesAsync(ct);

        // O arquivo antigo é compartilhado entre cofres: só sai quando ninguém mais o usa.
        if (!await db.Pokemon.IgnoreQueryFilters().AnyAsync(p => p.Sha256 == oldSha, ct))
        {
            var path = FilePath(oldSha);
            if (File.Exists(path))
                File.Delete(path);
        }
        return stored;
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
        // Todos os cofres (roda na inicialização, fora de qualquer conta).
        var stale = await db.Pokemon.IgnoreQueryFilters().Where(p => p.Legality == null || p.LegalityPkhexVersion != version || p.Pid == null).ToListAsync(ct);
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
