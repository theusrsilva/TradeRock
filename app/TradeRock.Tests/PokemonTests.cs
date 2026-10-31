using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using TradeRock.Core.Games;
using TradeRock.Core;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;

namespace TradeRock.Tests;

public sealed class Pk3ReaderTests
{
    [Fact]
    public void Le_os_pokemon_de_demonstracao()
    {
        var names = DemoPokemon.All.Select(d => Pk3Reader.Read(d.Data).Summary.SpeciesName).Order().ToList();
        Assert.Equal(["Deoxys", "Mewtwo"], names);
    }

    [Fact]
    public void Ek3_e_pk3_do_mesmo_pokemon_tem_o_mesmo_hash()
    {
        var plain = Pk3Reader.Read(DemoPokemon.All[0].Data);

        var pk = new PK3(plain.Data.ToArray());
        var encrypted = new byte[plain.Data.Length];
        if (plain.HasPartyData)
            pk.WriteEncryptedDataParty(encrypted);
        else
            pk.WriteEncryptedDataStored(encrypted);
        Assert.NotEqual(plain.Data, encrypted);

        var fromEk3 = Pk3Reader.Read(encrypted);
        Assert.Equal(plain.Sha256, fromEk3.Sha256);
        Assert.Equal(plain.Summary, fromEk3.Summary);
    }

    [Fact]
    public void Versao_de_caixa_80_bytes_e_aceita()
    {
        var party = Pk3Reader.Read(DemoPokemon.All[0].Data);
        var stored = party.Data.AsSpan(0, Pk3Reader.StoredSize).ToArray();
        var read = Pk3Reader.Read(stored);
        Assert.Equal(80, read.Data.Length);
        Assert.Equal(party.Summary.Species, read.Summary.Species);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(79)]
    [InlineData(101)]
    [InlineData(236)]
    public void Tamanho_errado_e_recusado(int size)
    {
        var ex = Assert.Throws<InvalidPokemonFileException>(() => Pk3Reader.Read(new byte[size]));
        Assert.Equal("size", ex.Code);
    }

    [Fact]
    public void Arquivo_corrompido_e_recusado()
    {
        var data = Pk3Reader.Read(DemoPokemon.All[0].Data).Data.ToArray();
        data[0x30] ^= 0xFF; // dentro dos blocos protegidos pelo checksum
        var ex = Assert.Throws<InvalidPokemonFileException>(() => Pk3Reader.Read(data));
        Assert.Equal("checksum", ex.Code);
    }
}

public sealed class PokemonStoreTests : IAsyncLifetime
{
    private readonly TestDataDir _dir = new();
    private PokemonStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _store = new PokemonStore(await _dir.CreateDbFactoryAsync(), _dir.Paths, TimeProvider.System);
    }

    public ValueTask DisposeAsync()
    {
        _dir.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Importar_duas_vezes_nao_duplica()
    {
        var data = DemoPokemon.All[0].Data;
        var first = await _store.ImportAsync(data, PokemonOrigin.Imported, "a.pk3", default);
        var second = await _store.ImportAsync(data, PokemonOrigin.Received, "b.pk3", default);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Pokemon.Id, second.Pokemon.Id);
        Assert.Single(await _store.ListAsync(default));
        Assert.True(File.Exists(_store.FilePath(first.Pokemon.Sha256, GameFamily.Frlg)));
    }

    [Fact]
    public async Task Leitura_confere_o_hash()
    {
        var imported = await _store.ImportAsync(DemoPokemon.All[1].Data, PokemonOrigin.Imported, null, default);
        var data = await _store.ReadDataAsync(imported.Pokemon, default);
        Assert.Equal(Pk3Reader.Read(DemoPokemon.All[1].Data).Data, data);

        var path = _store.FilePath(imported.Pokemon.Sha256, GameFamily.Frlg);
        var tampered = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        tampered[0x0A] ^= 1;
        await File.WriteAllBytesAsync(path, tampered, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ReadDataAsync(imported.Pokemon, default));
    }

    [Fact]
    public async Task Arquivo_apagado_e_regravado_na_reimportacao()
    {
        var imported = await _store.ImportAsync(DemoPokemon.All[0].Data, PokemonOrigin.Imported, null, default);
        File.Delete(_store.FilePath(imported.Pokemon.Sha256, GameFamily.Frlg));

        await _store.ImportAsync(DemoPokemon.All[0].Data, PokemonOrigin.Imported, null, default);

        Assert.True(File.Exists(_store.FilePath(imported.Pokemon.Sha256, GameFamily.Frlg)));
    }
}

/// <summary>Pasta temporária com banco migrado.</summary>
internal sealed class TestDataDir : IDisposable
{
    public AppPaths Paths { get; }

    public TestDataDir()
    {
        Paths = new AppPaths(Directory.CreateTempSubdirectory("traderock-test-").FullName);
        Paths.EnsureCreated();
    }

    public async Task<IDbContextFactory<TradeRockDb>> CreateDbFactoryAsync()
    {
        var options = new DbContextOptionsBuilder<TradeRockDb>()
            .UseSqlite($"Data Source={Paths.DatabasePath};Pooling=False").Options;
        var factory = new SimpleDbFactory(options);
        await using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        return factory;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Paths.DataDir, recursive: true);
        }
        catch (IOException)
        {
            // arquivo ainda aberto em algum teste: a pasta temporária fica para o sistema limpar
        }
    }

    private sealed class SimpleDbFactory(DbContextOptions<TradeRockDb> options) : IDbContextFactory<TradeRockDb>
    {
        public TradeRockDb CreateDbContext() => new(options);
    }
}

public sealed class TradeEvolutionTests
{
    [Theory]
    [InlineData(64, 0, 65)]     // Kadabra evolui sempre
    [InlineData(93, 13, 94)]    // Haunter com qualquer item
    [InlineData(95, 199, 208)]  // Onix com Metal Coat
    [InlineData(366, 193, 368)] // Clamperl com Deep Sea Scale
    public void Evolui(ushort species, ushort item, ushort target) =>
        Assert.Equal(target, TradeEvolution.EvolvesInto(species, item));

    [Theory]
    [InlineData(64, 195)] // Kadabra com Everstone
    [InlineData(95, 0)]   // Onix sem item
    [InlineData(123, 187)] // Scyther com o item errado
    [InlineData(150, 0)]  // Mewtwo
    public void Nao_evolui(ushort species, ushort item) =>
        Assert.Null(TradeEvolution.EvolvesInto(species, item));
}
