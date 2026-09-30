using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Saves;
using TradeRock.Core.Storage;

namespace TradeRock.Tests;

public sealed class VaultTests : IAsyncLifetime
{
    private readonly TestDataDir _dir = new();
    private IDbContextFactory<TradeRockDb> _db = null!;
    private PokemonStore _pokemon = null!;
    private VaultStore _vault = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await _dir.CreateDbFactoryAsync();
        _pokemon = new PokemonStore(_db, _dir.Paths, TimeProvider.System);
        _vault = new VaultStore(_db, _pokemon, TimeProvider.System);
    }

    public ValueTask DisposeAsync()
    {
        _dir.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Pokémon diferentes a partir do Mewtwo de demonstração (muda o apelido → outro hash).</summary>
    private async Task<StoredPokemon> Add(string nickname, byte level = 70)
    {
        var pk = new PK3(DemoPokemon.All.Single(d => d.Name == "mewtwo.pk3").Data.ToArray())
        {
            IsNicknamed = true,
            Nickname = nickname,
        };
        pk.CurrentLevel = level;
        pk.RefreshChecksum();
        var data = new byte[100];
        pk.WriteDecryptedDataParty(data);
        return (await _pokemon.ImportAsync(data, PokemonOrigin.Imported, null, default)).Pokemon;
    }

    [Fact]
    public async Task Novos_chegam_na_caixa_de_entrada()
    {
        await Add("A");
        var overview = await _vault.OverviewAsync(default);
        Assert.Equal(1, overview.InboxCount);
        Assert.Empty(overview.Boxes);
    }

    [Fact]
    public async Task Caixas_criar_renomear_reordenar_e_apagar()
    {
        var a = await _vault.CreateBoxAsync("Lendários", default);
        var b = await _vault.CreateBoxAsync("Trocas", default);
        await _vault.RenameBoxAsync(a.Id, "Míticos", default);
        await _vault.ReorderBoxesAsync([b.Id, a.Id], default);

        var boxes = (await _vault.OverviewAsync(default)).Boxes;
        Assert.Equal(["Trocas", "Míticos"], boxes.Select(x => x.Name));

        var mon = await Add("A");
        await _vault.MoveAsync(mon.Id, a.Id, null, default);
        var ex = await Assert.ThrowsAsync<VaultException>(() => _vault.DeleteBoxAsync(a.Id, default));
        Assert.Equal("box_not_empty", ex.Code);

        await _vault.DeleteBoxAsync(b.Id, default);
        Assert.Single((await _vault.OverviewAsync(default)).Boxes);

        Assert.Equal("name_invalid", (await Assert.ThrowsAsync<VaultException>(() => _vault.CreateBoxAsync("  ", default))).Code);
    }

    [Fact]
    public async Task Mover_para_posicao_ocupada_troca_os_dois()
    {
        var box = await _vault.CreateBoxAsync("Caixa 1", default);
        var a = await Add("A");
        var b = await Add("B");

        await _vault.MoveAsync(a.Id, box.Id, 0, default);
        await _vault.MoveAsync(b.Id, box.Id, 5, default);
        await _vault.MoveAsync(a.Id, box.Id, 5, default); // A vai para onde B está

        var inBox = await _vault.SearchAsync(new VaultQuery { Scope = box.Id.ToString() }, default);
        Assert.Equal(0, inBox.Single(p => p.Id == b.Id).Slot);
        Assert.Equal(5, inBox.Single(p => p.Id == a.Id).Slot);
    }

    [Fact]
    public async Task Vindo_da_caixa_de_entrada_o_outro_vai_para_a_entrada()
    {
        var box = await _vault.CreateBoxAsync("Caixa 1", default);
        var a = await Add("A");
        var b = await Add("B");
        await _vault.MoveAsync(a.Id, box.Id, 3, default);

        await _vault.MoveAsync(b.Id, box.Id, 3, default);

        var inbox = await _vault.SearchAsync(new VaultQuery { Scope = "inbox" }, default);
        Assert.Contains(inbox, p => p.Id == a.Id);
    }

    [Fact]
    public async Task Sem_posicao_vai_para_a_primeira_livre_e_caixa_cheia_recusa()
    {
        var box = await _vault.CreateBoxAsync("Cheia", default);
        for (var i = 0; i < VaultBox.Slots; i++)
            await _vault.MoveAsync((await Add($"M{i}")).Id, box.Id, null, default);

        var slots = (await _vault.SearchAsync(new VaultQuery { Scope = box.Id.ToString() }, default)).Select(p => p.Slot!.Value);
        Assert.Equal(Enumerable.Range(0, 30), slots.Order());

        var extra = await Add("Extra");
        Assert.Equal("box_full", (await Assert.ThrowsAsync<VaultException>(() => _vault.MoveAsync(extra.Id, box.Id, null, default))).Code);
        Assert.Equal("slot_invalid", (await Assert.ThrowsAsync<VaultException>(() => _vault.MoveAsync(extra.Id, box.Id, 30, default))).Code);
    }

    [Fact]
    public async Task Busca_por_texto_numero_e_filtros()
    {
        var a = await Add("Bolinha", 70);
        await Add("Outro", 80);

        Assert.Single(await _vault.SearchAsync(new VaultQuery { Text = "bolin" }, default));
        Assert.Equal(2, (await _vault.SearchAsync(new VaultQuery { Text = "#150" }, default)).Count);
        Assert.Equal(2, (await _vault.SearchAsync(new VaultQuery { Text = "mewtwo" }, default)).Count);
        Assert.Single(await _vault.SearchAsync(new VaultQuery { MinLevel = 75 }, default));
        Assert.Empty(await _vault.SearchAsync(new VaultQuery { Shiny = true }, default));
        Assert.Equal(2, (await _vault.SearchAsync(new VaultQuery { Origin = PokemonOrigin.Imported }, default)).Count);

        var tag = await _vault.SaveTagAsync(null, "Para trocar", "sky", default);
        await _vault.SetTagsAsync([a.Id], [tag.Id], [], default);
        var tagged = await _vault.SearchAsync(new VaultQuery { TagIds = [tag.Id] }, default);
        Assert.Equal(a.Id, Assert.Single(tagged).Id);
    }

    [Fact]
    public async Task Tags_em_lote_e_nome_repetido()
    {
        var a = await Add("A");
        var b = await Add("B");
        var t1 = await _vault.SaveTagAsync(null, "Competitivo", "red", default);
        var t2 = await _vault.SaveTagAsync(null, "Shiny hunt", "amber", default);

        await _vault.SetTagsAsync([a.Id, b.Id], [t1.Id, t2.Id], [], default);
        await _vault.SetTagsAsync([a.Id, b.Id], [], [t2.Id], default);
        await _vault.SetTagsAsync([a.Id], [t1.Id], [], default); // repetido não duplica

        var all = await _vault.SearchAsync(new VaultQuery(), default);
        Assert.All(all, p => Assert.Equal([t1.Id], p.Tags.Select(t => t.TagId)));

        Assert.Equal("tag_exists", (await Assert.ThrowsAsync<VaultException>(() => _vault.SaveTagAsync(null, "Competitivo", "sky", default))).Code);
        Assert.Equal("color_invalid", (await Assert.ThrowsAsync<VaultException>(() => _vault.SaveTagAsync(null, "Nova", "rosa-choque", default))).Code);

        await _vault.DeleteTagAsync(t1.Id, default);
        Assert.All(await _vault.SearchAsync(new VaultQuery(), default), p => Assert.Empty(p.Tags));
    }

    [Fact]
    public async Task Lixeira_restaurar_e_reimportar()
    {
        var box = await _vault.CreateBoxAsync("Caixa", default);
        var a = await Add("A");
        await _vault.MoveAsync(a.Id, box.Id, 2, default);

        await _vault.TrashAsync([a.Id], default);
        Assert.Empty(await _vault.SearchAsync(new VaultQuery(), default));
        var trashed = Assert.Single(await _vault.SearchAsync(new VaultQuery { Scope = "trash" }, default));
        Assert.Null(trashed.BoxId);
        Assert.Empty(await _pokemon.ListAsync(default)); // lixeira fica fora das listas (ex.: time da troca)
        Assert.Equal("pokemon_in_trash", (await Assert.ThrowsAsync<VaultException>(() => _vault.MoveAsync(a.Id, box.Id, 0, default))).Code);

        await _vault.RestoreAsync([a.Id], default);
        Assert.Single(await _vault.SearchAsync(new VaultQuery { Scope = "inbox" }, default));

        // Importar de novo um arquivo que está na lixeira traz ele de volta.
        await _vault.TrashAsync([a.Id], default);
        var data = await _pokemon.ReadDataAsync(a, default);
        var again = await _pokemon.ImportAsync(data, PokemonOrigin.Imported, null, default);
        Assert.True(again.Restored);
        Assert.Null(again.Pokemon.DeletedAt);
    }

    [Fact]
    public async Task Esvaziar_a_lixeira_preserva_quem_esta_no_historico()
    {
        var kept = await Add("Historico");
        var gone = await Add("Sai");
        await using (var db = await _db.CreateDbContextAsync())
        {
            db.Trades.Add(new TradeRecord
            {
                Id = Guid.NewGuid(), EngineId = "fake", TrainerName = "EMU", EventsJson = "[]",
                Entries = [new TradeRecordEntry { SentPokemonId = kept.Id }],
            });
            await db.SaveChangesAsync();
        }

        await _vault.TrashAsync([kept.Id, gone.Id], default);
        var result = await _vault.EmptyTrashAsync(default);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.KeptBecauseOfHistory);
        Assert.False(File.Exists(_pokemon.FilePath(gone.Sha256)));
        Assert.True(File.Exists(_pokemon.FilePath(kept.Sha256)));
        Assert.NotNull(await _pokemon.GetAsync(kept.Id, default));
    }

    [Fact]
    public async Task Mesmo_pokemon_com_dados_diferentes_e_possivel_duplicata()
    {
        var level70 = await Add("Mesmo", 70);
        var level80 = await Add("Mesmo", 80); // mesmo PID e OT, outro nível → outro hash
        var other = await Add("Mesmo", 70); // arquivo idêntico: não entra de novo

        Assert.Equal(level70.Id, other.Id);
        var dupes = await _vault.PossibleDuplicatesAsync(default);
        Assert.Contains(level70.Id, dupes);
        Assert.Contains(level80.Id, dupes);
    }
}

public sealed class Pk3ExportTests
{
    private static byte[] Mewtwo => Pk3Reader.Read(DemoPokemon.All.Single(d => d.Name == "mewtwo.pk3").Data).Data;

    [Theory]
    [InlineData("pk3", 100)]
    [InlineData("pk3", 80)]
    [InlineData("ek3", 100)]
    [InlineData("ek3", 80)]
    public void Exporta_e_le_de_volta_o_mesmo_pokemon(string format, int size)
    {
        var file = Pk3Export.ToFile(Mewtwo, format, size);
        Assert.Equal(size, file.Length);

        var read = Pk3Reader.Read(file);
        Assert.Equal(150, read.Summary.Species);
        Assert.Equal(Pk3Reader.Read(Mewtwo).Data[..80], read.Data[..80]);
        if (format == "ek3")
            Assert.NotEqual(Mewtwo[..80], file[..80]); // criptografado de verdade
    }

    [Fact]
    public void Formato_invalido_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => Pk3Export.ToFile(Mewtwo, "pk4", 100));
        Assert.Throws<ArgumentException>(() => Pk3Export.ToFile(Mewtwo, "pk3", 90));
    }
}

public sealed class SaveInjectionTests
{
    private static byte[] Mewtwo => Pk3Reader.Read(DemoPokemon.All.Single(d => d.Name == "mewtwo.pk3").Data).Data;
    private static byte[] Deoxys => Pk3Reader.Read(DemoPokemon.All.Single(d => d.Name == "deoxys.pk3").Data).Data;

    [Fact]
    public void Descreve_um_save_vazio()
    {
        var layout = SaveInjection.Describe(SaveFixture.BlankFireRed());
        Assert.Equal("RED", layout.TrainerName);
        Assert.Equal(14, layout.BoxCount);
        Assert.Equal(30, layout.SlotsPerBox);
        Assert.Equal(14, layout.BoxNames.Count);
        Assert.Empty(layout.Occupied);
    }

    [Fact]
    public void Injeta_e_confere_relendo_pelo_pkhex()
    {
        var result = SaveInjection.Inject(SaveFixture.BlankFireRed(), [new(Mewtwo, 0, 0), new(Deoxys, 3, 29)]);

        var layout = SaveInjection.Describe(result);
        Assert.Contains(layout.Occupied, s => s is { Box: 0, Slot: 0, Species: 150 });
        Assert.Contains(layout.Occupied, s => s is { Box: 3, Slot: 29, Species: 386 });

        // O save continua sendo o mesmo jogo/treinador e a Pokédex marca os injetados como capturados.
        var sav = SaveUtil.GetSaveFile(result)!;
        Assert.Equal("RED", sav.OT);
        Assert.True(sav.GetCaught(150));
    }

    [Fact]
    public void Nao_sobrescreve_posicao_ocupada_nem_repete_posicao()
    {
        var once = SaveInjection.Inject(SaveFixture.BlankFireRed(), [new(Mewtwo, 0, 0)]);

        Assert.Equal("slot_occupied", Assert.Throws<SaveInjectionException>(() => SaveInjection.Inject(once, [new(Deoxys, 0, 0)])).Code);
        Assert.Equal("slot_repeated", Assert.Throws<SaveInjectionException>(() =>
            SaveInjection.Inject(once, [new(Deoxys, 1, 1), new(Mewtwo, 1, 1)])).Code);
        Assert.Equal("slot_invalid", Assert.Throws<SaveInjectionException>(() => SaveInjection.Inject(once, [new(Deoxys, 14, 0)])).Code);
        Assert.Equal("nothing_to_inject", Assert.Throws<SaveInjectionException>(() => SaveInjection.Inject(once, [])).Code);
    }

    [Fact]
    public void Arquivo_que_nao_e_save_e_recusado()
    {
        Assert.Equal("sav_unknown", Assert.Throws<SaveInjectionException>(() => SaveInjection.Describe(new byte[0x20000])).Code);
    }

    [Fact]
    public void Backup_guarda_o_original_com_data_e_hash()
    {
        var dir = Directory.CreateTempSubdirectory("traderock-backup-").FullName;
        try
        {
            var backups = new SaveBackups(new TradeRock.Core.AppPaths(dir), TimeProvider.System);
            var original = SaveFixture.BlankFireRed();
            var path = backups.Create(original, "FireRed (EUA).sav");

            Assert.StartsWith(backups.Directory, path);
            Assert.EndsWith(".sav", path);
            Assert.Contains(SaveInjection.Sha256(original)[..8], path);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Leitura_do_save_inteiro_para_importar_no_cofre()
    {
        var withMons = SaveInjection.Inject(SaveFixture.BlankFireRed(), [new(Mewtwo, 1, 4)]);
        var contents = SaveImport.Read(withMons);
        var entry = Assert.Single(contents.Entries);
        Assert.Equal("box", entry.Place);
        Assert.Equal((1, 4), (entry.Box, entry.Slot));
        Assert.Equal(150, entry.Summary.Species);
    }
}
