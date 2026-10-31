using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using PKHeX.Core;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Games;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.Tests;

/// <summary>Pokémon de teste dos jogos novos, montados com o PKHeX.</summary>
internal static class SwitchPokemon
{
    public static PKM Make(GameFamily family, ushort species = 25, string ot = "ROCHA", uint pid = 0x12345678)
    {
        var game = GameCatalog.Get(family);
        var pk = (PKM)Activator.CreateInstance(game.EntityType)!;
        pk.Species = species;
        pk.CurrentLevel = 10;
        pk.OriginalTrainerName = ot;
        pk.TID16 = 0xE240; // ID32 = 0x0001E240 = 123456: TID de 6 dígitos 123456, SID 0
        pk.SID16 = 0x0001;
        pk.PID = pid;
        pk.EncryptionConstant = pid;
        pk.Language = (int)LanguageID.English;
        pk.Version = GameCatalog.ToVersion(game.Versions[0]);
        pk.Nickname = SpeciesName.GetSpeciesNameGeneration(species, pk.Language, pk.Format);
        pk.ResetPartyStats(); // da Gen 8 em diante o PS atual fica na parte "de caixa" do arquivo
        pk.RefreshChecksum();
        return pk;
    }

    public static byte[] Stored(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return data;
    }

    public static byte[] EncryptedParty(PKM pk)
    {
        pk.ResetPartyStats();
        var data = new byte[pk.SIZE_PARTY];
        pk.WriteEncryptedDataParty(data);
        return data;
    }
}

public sealed class GameCatalogTests
{
    [Fact]
    public void Todo_jogo_tem_versoes_e_formato()
    {
        Assert.Equal(Enum.GetValues<GameFamily>().Length, GameCatalog.All.Count);
        foreach (var version in Enum.GetValues<TradeGame>())
            Assert.Contains(version, GameCatalog.Of(version).Versions);
        Assert.Equal(GameFamily.Sv, GameCatalog.ByExtension(".PK9")!.Family);
        Assert.Equal(GameFamily.Za, GameCatalog.ByKey("za")!.Family);
        Assert.True(GameCatalog.Get(GameFamily.Sv).HasSpecies(906)); // Sprigatito
        Assert.False(GameCatalog.Get(GameFamily.Frlg).HasSpecies(906));
        Assert.True(GameCatalog.Get(GameFamily.Swsh).LanMode);
        Assert.False(GameCatalog.Get(GameFamily.Sv).LanMode); // D21: a troca do SV não usa LAN
    }

    [Theory]
    [InlineData(123456u, 1234u)]
    [InlineData(0u, 0u)]
    [InlineData(967295u, 4294u)] // ID32 máximo
    public void Ids_de_6_digitos_vao_e_voltam(uint tid, uint sid)
    {
        var sv = GameCatalog.Get(GameFamily.Sv);
        var ids = TrainerIds.FromDisplay(sv, tid, sid)!.Value;
        Assert.Equal((tid, sid), TrainerIds.Display(sv, ids.Tid16, ids.Sid16));
    }

    [Fact]
    public void Ids_que_nao_cabem_sao_recusados()
    {
        Assert.Null(TrainerIds.FromDisplay(GameCatalog.Get(GameFamily.Sv), 1_000_000, 0));
        Assert.Null(TrainerIds.FromDisplay(GameCatalog.Get(GameFamily.Sv), 999999, 4295));
        Assert.Null(TrainerIds.FromDisplay(GameCatalog.Get(GameFamily.Frlg), 70000, 0));
        Assert.Equal(((ushort)54321, (ushort)12345), TrainerIds.FromDisplay(GameCatalog.Get(GameFamily.Frlg), 54321, 12345));
    }

    [Theory]
    [InlineData(LinkCodeKind.Digits8, "12345678", true)]
    [InlineData(LinkCodeKind.Digits8, "1234567", false)]
    [InlineData(LinkCodeKind.Digits8, "1234567a", false)]
    [InlineData(LinkCodeKind.Digits8, null, true)]
    [InlineData(LinkCodeKind.ThreePokemon, "0,4,9", true)]
    [InlineData(LinkCodeKind.ThreePokemon, "0,4", false)]
    [InlineData(LinkCodeKind.None, "12345678", false)]
    public void Codigo_de_troca(LinkCodeKind kind, string? code, bool valid) =>
        Assert.Equal(valid, TradeRequest.IsValidLinkCode(kind, code));

    [Fact]
    public void Pedido_dos_jogos_novos_aceita_uma_oferta_e_nome_maior()
    {
        var data = SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Sv));
        var request = new TradeRequest([new PartyMember(Guid.NewGuid(), data, "Pikachu")], [0], TradeGame.Scarlet,
            new TrainerProfile("Treinadora", 1, 2), LinkCode: "12345678");
        Assert.Empty(request.Validate());

        // No FRLG continua valendo o time de 2 e o nome de 7 caracteres da tabela Gen III.
        var frlg = request with { Game = TradeGame.FireRed, LinkCode = null };
        Assert.Contains("party_size", frlg.Validate());
        Assert.Contains("trainer_name_too_long", frlg.Validate());
    }
}

public sealed class PokemonReaderTests
{
    [Theory]
    [InlineData(GameFamily.Lgpe, "x.pb7")]
    [InlineData(GameFamily.Swsh, "x.pk8")]
    [InlineData(GameFamily.Bdsp, "x.pb8")]
    [InlineData(GameFamily.Pla, "x.pa8")]
    [InlineData(GameFamily.Sv, "x.pk9")]
    [InlineData(GameFamily.Za, "x.pa9")]
    public void Le_cada_formato_criptografado_ou_nao_com_o_mesmo_hash(GameFamily family, string fileName)
    {
        var pk = SwitchPokemon.Make(family);
        var plain = PokemonReader.Read(SwitchPokemon.Stored(pk), fileName: fileName);
        var encrypted = PokemonReader.Read(SwitchPokemon.EncryptedParty(pk.Clone()), fileName: fileName);

        Assert.Equal(family, plain.Summary.Family);
        Assert.Equal(25, plain.Summary.Species);
        Assert.Equal("ROCHA", plain.Summary.OtName);
        Assert.Equal(pk.SIZE_STORED, plain.Data.Length);
        Assert.Equal(plain.Sha256, encrypted.Sha256);
        // O jogo também pode vir de quem chamou (Pokémon recebido numa troca).
        Assert.Equal(plain.Sha256, PokemonReader.Read(SwitchPokemon.Stored(pk), family).Sha256);
    }

    [Theory]
    [InlineData(GameFamily.Swsh)]
    [InlineData(GameFamily.Bdsp)]
    [InlineData(GameFamily.Sv)]
    public void Formato_ambiguo_sem_extensao_e_recusado(GameFamily family)
    {
        var ex = Assert.Throws<InvalidPokemonFileException>(() =>
            PokemonReader.Read(SwitchPokemon.Stored(SwitchPokemon.Make(family))));
        Assert.Equal("format_ambiguous", ex.Code);
    }

    [Fact]
    public void Arceus_e_reconhecido_sem_extensao()
    {
        var read = PokemonReader.Read(SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Pla)));
        Assert.Equal(GameFamily.Pla, read.Summary.Family);
    }

    [Fact]
    public void Extensao_de_outro_jogo_e_recusada()
    {
        var ex = Assert.Throws<InvalidPokemonFileException>(() =>
            PokemonReader.Read(SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Lgpe)), fileName: "x.pk9"));
        Assert.Equal("game_mismatch", ex.Code);
    }

    [Fact]
    public void Gen3_continua_igual_ao_Pk3Reader()
    {
        var data = DemoPokemon.All[0].Data;
        var read = PokemonReader.Read(data);
        Assert.Equal(GameFamily.Frlg, read.Summary.Family);
        Assert.Equal(Pk3Reader.Read(data).Sha256, read.Sha256);
    }

    [Fact]
    public void Checksum_errado_e_recusado()
    {
        var data = SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Sv));
        data[0x60] ^= 0xFF;
        var ex = Assert.Throws<InvalidPokemonFileException>(() => PokemonReader.Read(data, GameFamily.Sv));
        Assert.Equal("checksum", ex.Code);
    }
}

public sealed class MultiGameStoreTests : IAsyncLifetime
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
    public async Task Guarda_com_o_jogo_e_a_extensao_do_formato()
    {
        var pk = SwitchPokemon.Make(GameFamily.Za);
        var imported = await _store.ImportAsync(SwitchPokemon.EncryptedParty(pk), PokemonOrigin.Received, null, default, GameFamily.Za);

        Assert.Equal(GameFamily.Za, imported.Pokemon.Family);
        Assert.NotNull(imported.Pokemon.Legality);
        var path = _store.FilePath(imported.Pokemon.Sha256, GameFamily.Za);
        Assert.EndsWith(".pa9", path);
        Assert.True(File.Exists(path));
        Assert.Equal(SwitchPokemon.Stored(pk), await _store.ReadDataAsync(imported.Pokemon, default));

        // A edição não muda o jogo do Pokémon.
        var other = SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Lgpe));
        var ex = await Assert.ThrowsAsync<InvalidPokemonFileException>(() => _store.ReplaceAsync(imported.Pokemon.Id, other, default));
        Assert.Equal("game_mismatch", ex.Code);
    }
}

/// <summary>Ponta a ponta pela API: cofre, "Meu treinador" e troca simulada nos jogos novos.</summary>
public sealed class MultiGameApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private WebApplication _app = null!;
    private string _dataDir = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("traderock-multi-").FullName;
        _app = TradeRockServer.Create(
            ["--data-dir", _dataDir, "--Engines:Fake:SpeedFactor=0"], _token, b => b.WebHost.UseTestServer());
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("http://127.0.0.1/");
        _client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<JsonElement> UploadAsync(byte[] data, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "files", fileName);
        var response = await _client.PostAsync("/api/pokemon", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();
    }

    [Fact]
    public async Task Upload_de_pk9_mostra_o_jogo_e_o_id_de_6_digitos()
    {
        var result = await UploadAsync(SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Sv)), "pikachu.pk9");
        Assert.True(result.GetProperty("ok").GetBoolean());
        var p = result.GetProperty("pokemon");
        Assert.Equal("sv", p.GetProperty("family").GetString());
        Assert.Equal(123456u, p.GetProperty("tid").GetUInt32());
        Assert.Equal(0u, p.GetProperty("sid").GetUInt32());
        Assert.Empty(p.GetProperty("warnings").EnumerateArray()); // regras da Gen 3 não valem aqui

        var vault = await _client.GetFromJsonAsync<JsonElement>("/api/vault/pokemon?game=sv");
        Assert.Equal("sv", Assert.Single(vault.EnumerateArray()).GetProperty("family").GetString());

        // Exporta no formato do próprio jogo.
        var file = await _client.GetAsync($"/api/pokemon/{p.GetProperty("id").GetGuid()}/file");
        Assert.EndsWith(".pk9", file.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
    }

    [Fact]
    public async Task Ambiguo_sem_extensao_explica_o_erro()
    {
        var result = await UploadAsync(SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Swsh)), "pikachu.bin");
        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("format_ambiguous", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Meu_treinador_e_por_jogo()
    {
        var put = await _client.PutAsJsonAsync("/api/settings/my-trainer?game=sv",
            new { name = "Treinadora", tid = 123456, sid = 1234, gender = 1 });
        put.EnsureSuccessStatusCode();
        var sv = await _client.GetFromJsonAsync<JsonElement>("/api/settings/my-trainer?game=sv");
        Assert.Equal(123456u, sv.GetProperty("tid").GetUInt32());
        Assert.Equal(1234u, sv.GetProperty("sid").GetUInt32());
        Assert.Equal("Treinadora", sv.GetProperty("name").GetString());

        // O do FRLG continua separado (e vazio).
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/settings/my-trainer")).StatusCode);
        // 10 caracteres não cabem no FRLG.
        var frlg = await _client.PutAsJsonAsync("/api/settings/my-trainer?game=frlg", new { name = "Treinadora", tid = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, frlg.StatusCode);
        var bad = await _client.PutAsJsonAsync("/api/settings/my-trainer?game=sv", new { name = "Ana", tid = 1000000, sid = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Troca_simulada_no_scarlet_recebe_um_pk9()
    {
        var uploaded = await UploadAsync(SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Sv)), "pikachu.pk9");
        var id = uploaded.GetProperty("pokemon").GetProperty("id").GetGuid();

        var start = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { id },
            offeredSlots = new[] { 0 },
            game = "scarlet",
            trainer = new { name = "Treinadora", tid = 1, sid = 2 },
            simulation = new { scenario = "success" },
            linkCode = "12345678",
            acceptIllegal = true,
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        JsonElement history = default;
        for (var i = 0; i < 100; i++)
        {
            history = await _client.GetFromJsonAsync<JsonElement>("/api/trades");
            if (history.GetArrayLength() > 0)
                break;
            await Task.Delay(50);
        }
        var item = Assert.Single(history.EnumerateArray());
        Assert.Equal("completed", item.GetProperty("status").GetString());
        Assert.Equal("scarlet", item.GetProperty("game").GetString());
        var received = Assert.Single(item.GetProperty("entries").EnumerateArray()).GetProperty("receivedPokemonId").GetGuid();
        var pokemon = await _client.GetFromJsonAsync<JsonElement>($"/api/pokemon/{received}");
        Assert.Equal("sv", pokemon.GetProperty("family").GetString());
        Assert.Equal("DEMO", pokemon.GetProperty("otName").GetString());
    }

    [Fact]
    public async Task Construtor_gera_salva_e_reabre_um_pokemon_do_scarlet()
    {
        var data = await _client.GetFromJsonAsync<JsonElement>("/api/builder/data?game=scarlet");
        Assert.Equal(252, data.GetProperty("maxEv").GetInt32());
        Assert.False(data.GetProperty("advanced").GetBoolean());
        var raichu = await _client.GetFromJsonAsync<JsonElement>("/api/builder/species/26?game=scarlet");
        Assert.True(raichu.GetProperty("forms").GetArrayLength() >= 2);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/builder/species/906?game=fire_red")).StatusCode);

        await _client.PutAsJsonAsync("/api/settings/my-trainer?game=sv", new { name = "Treinadora", tid = 123456, sid = 1234 });
        var trainer = await _client.GetFromJsonAsync<JsonElement>("/api/builder/default-trainer?game=scarlet");
        Assert.Equal(123456u, trainer.GetProperty("tid").GetUInt32());

        var generated = await _client.PostAsJsonAsync("/api/builder/generate", new
        {
            spec = new { species = 25, level = 30, game = "scarlet", trainer },
            language = "pt-BR",
        });
        generated.EnsureSuccessStatusCode();
        var result = await generated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sv", result.GetProperty("summary").GetProperty("family").GetString());

        var saved = await _client.PostAsJsonAsync("/api/builder/save", new { data = result.GetProperty("data").GetString(), game = "scarlet" });
        saved.EnsureSuccessStatusCode();
        var pokemon = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pokemon");
        Assert.Equal("sv", pokemon.GetProperty("family").GetString());
        Assert.Equal(123456u, pokemon.GetProperty("tid").GetUInt32());

        var reopened = await _client.GetFromJsonAsync<JsonElement>($"/api/builder/from/{pokemon.GetProperty("id").GetGuid()}");
        Assert.Equal("scarlet", reopened.GetProperty("spec").GetProperty("game").GetString());
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("raw").ValueKind);
    }

    [Fact]
    public async Task Mover_do_scarlet_para_o_sword_no_cofre()
    {
        var generated = await _client.PostAsJsonAsync("/api/builder/generate", new
        {
            spec = new { species = 25, level = 30, game = "scarlet", trainer = new { name = "ROCHA", tid = 123456, sid = 1234 } },
        });
        var data = (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetString();
        var saved = await _client.PostAsJsonAsync("/api/builder/save", new { data, game = "scarlet" });
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pokemon").GetProperty("id").GetGuid();

        var destinations = await _client.GetFromJsonAsync<JsonElement>($"/api/vault/pokemon/{id}/destinations");
        var swsh = destinations.EnumerateArray().Single(d => d.GetProperty("game").GetString() == "swsh");
        Assert.Equal(JsonValueKind.Null, swsh.GetProperty("refusal").ValueKind);

        await _client.PutAsJsonAsync("/api/settings/my-trainer?game=swsh", new { name = "Treinadora", tid = 654321, sid = 4321 });
        var moved = await _client.PostAsJsonAsync($"/api/vault/pokemon/{id}/transfer", new { game = "swsh" });
        moved.EnsureSuccessStatusCode();
        var pokemon = (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pokemon");
        Assert.Equal(id, pokemon.GetProperty("id").GetGuid()); // o mesmo registro do cofre
        Assert.Equal("swsh", pokemon.GetProperty("family").GetString());
        Assert.NotEqual("illegal", pokemon.GetProperty("legality").GetString());

        var refused = await _client.PostAsJsonAsync($"/api/vault/pokemon/{id}/transfer", new { game = "lgpe" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("home_no_destination", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pokemon_de_outro_jogo_e_codigo_invalido_sao_recusados()
    {
        var demo = (await _client.GetFromJsonAsync<JsonElement>("/api/pokemon")).EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).First();
        var wrongGame = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { demo },
            offeredSlots = new[] { 0 },
            game = "scarlet",
            trainer = new { name = "Ana", tid = 1, sid = 2 },
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongGame.StatusCode);
        Assert.Contains("pokemon_wrong_game", await wrongGame.Content.ReadAsStringAsync());

        var uploaded = await UploadAsync(SwitchPokemon.Stored(SwitchPokemon.Make(GameFamily.Za)), "pikachu.pa9");
        var badCode = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { uploaded.GetProperty("pokemon").GetProperty("id").GetGuid() },
            offeredSlots = new[] { 0 },
            game = "legends_za",
            trainer = new { name = "Ana", tid = 1, sid = 2 },
            linkCode = "123",
            acceptIllegal = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
        Assert.Contains("link_code_invalid", await badCode.Content.ReadAsStringAsync());
    }
}

/// <summary>Construtor nos jogos novos (Entrega 14b): listas por jogo e geração pelo AutoMod no formato do jogo.</summary>
public sealed class MultiGameBuilderTests
{
    private static readonly TrainerSpec Me = new("ROCHA", 123456, 1234);

    [Fact]
    public void Listas_sao_do_jogo()
    {
        var sv = BuilderData.For(GameCatalog.Get(GameFamily.Sv));
        Assert.Contains(sv.Species, s => s.Value == 906); // Sprigatito
        Assert.Contains(sv.Moves, m => m.Text == "Tera Blast");
        Assert.Equal(252, sv.MaxEv);
        Assert.Equal(12, sv.NicknameLength);
        Assert.Contains(sv.Items, i => i.Text == "Leftovers");

        var frlg = BuilderData.For(GameCatalog.Get(GameFamily.Frlg));
        Assert.DoesNotContain(frlg.Species, s => s.Value == 906);
        Assert.Equal(255, frlg.MaxEv);
    }

    [Fact]
    public void Especie_traz_habilidade_oculta_e_formas()
    {
        var raichu = BuilderData.Details(26, GameCatalog.Get(GameFamily.Sv));
        Assert.Contains(raichu.Forms!, f => f.Value == 1); // Raichu de Alola existe no SV
        Assert.Contains(raichu.Abilities, a => a.Value == 2); // habilidade oculta
    }

    [Theory]
    [InlineData(TradeGame.Scarlet)]
    [InlineData(TradeGame.Sword)]
    [InlineData(TradeGame.BrilliantDiamond)]
    [InlineData(TradeGame.LegendsArceus)]
    [InlineData(TradeGame.LegendsZA)]
    [InlineData(TradeGame.LetsGoPikachu)]
    public async Task Gera_pikachu_legal_no_formato_do_jogo(TradeGame game)
    {
        var spec = new PokemonSpec { Species = 25, Level = 30, Game = game, Trainer = Me };
        Assert.Empty(spec.Validate());
        var result = await PokemonBuilder.GenerateAsync(spec, "pt-BR", default);

        Assert.True(result.Usable, $"{result.Generation}: {string.Join("; ", result.Legality?.Problems.Select(p => p.Message) ?? [])}");
        var family = GameCatalog.Of(game).Family;
        Assert.Equal(family, result.Summary!.Family);
        var read = PokemonReader.Read(result.Data!, family);
        Assert.Equal(25, read.Summary.Species);
        Assert.Equal("ROCHA", read.Summary.OtName);
        // O treinador volta com os números que o jogo mostra.
        Assert.Equal((123456u, 1234u), (result.Obtained!.Trainer.Tid, result.Obtained.Trainer.Sid));
        Assert.Null(result.Raw); // modo avançado só no FRLG
    }

    [Fact]
    public void Limites_do_jogo_na_validacao()
    {
        var sv = new PokemonSpec { Species = 25, Game = TradeGame.Scarlet, Trainer = Me, Evs = [253, 0, 0, 0, 0, 0] };
        Assert.Contains("evs_invalid", sv.Validate());
        // Sprigatito não existe no Let's Go.
        Assert.Contains("species_invalid", (sv with { Evs = [0, 0, 0, 0, 0, 0], Species = 906, Game = TradeGame.LetsGoPikachu }).Validate());
        Assert.Contains("trainer_id_invalid", (sv with { Evs = [0, 0, 0, 0, 0, 0], Trainer = new("ROCHA", 1_000_000, 0) }).Validate());
        // No FRLG o TID de 6 dígitos não cabe.
        Assert.Contains("trainer_id_invalid", new PokemonSpec { Species = 25, Trainer = Me }.Validate());
    }
}

/// <summary>Mover entre jogos como o HOME (Entrega 14d).</summary>
public sealed class HomeTransferTests
{
    private static readonly SimpleTrainerInfo Owner = new(GameVersion.SL) { OT = "ROCHA", TID16 = 0xE240, SID16 = 1, Language = 2 };

    private static async Task<PKM> LegalAsync(TradeGame game, ushort species = 25, byte level = 30)
    {
        var trainer = GameCatalog.Of(game).SixDigitTrainerId ? new TrainerSpec("ROCHA", 123456, 1234) : new TrainerSpec("ROCHA", 24680, 13579);
        var result = await PokemonBuilder.GenerateAsync(new PokemonSpec { Species = species, Level = level, Game = game, Trainer = trainer }, "en", default);
        Assert.True(result.Usable);
        return PokemonReader.ToEntity(result.Data!, GameCatalog.Of(game).Family);
    }

    [Fact]
    public async Task Do_firered_para_o_scarlet()
    {
        var pk = await LegalAsync(TradeGame.FireRed, 25, 10);
        var moved = HomeTransfer.Move(pk, GameCatalog.Get(GameFamily.Sv), Owner);
        Assert.True(moved.Ok, moved.Refusal);
        Assert.IsType<PK9>(moved.Pokemon);
        Assert.Equal(25, moved.Pokemon!.Species);
        // Sem o rastreador do HOME: "não verificável", nunca "ilegal" por isso.
        var report = LegalityReporter.Analyze(moved.Pokemon, "pt-BR");
        Assert.NotEqual(LegalityStatus.Illegal, report.Status);
    }

    [Fact]
    public async Task Do_scarlet_para_o_sword_e_recusas()
    {
        var pk = await LegalAsync(TradeGame.Scarlet);
        var moved = HomeTransfer.Move(pk, GameCatalog.Get(GameFamily.Swsh), Owner);
        Assert.True(moved.Ok, moved.Refusal);
        Assert.IsType<PK8>(moved.Pokemon);
        var report = LegalityReporter.Analyze(moved.Pokemon!, "en");
        Assert.True(report.Status != LegalityStatus.Illegal, string.Join(" | ", report.Problems.Select(p => $"{p.Identifier}/{p.Code}: {p.Message}")));

        var destinations = HomeTransfer.Destinations(pk, GameFamily.Sv);
        Assert.Equal("same_game", destinations[GameFamily.Sv]);
        Assert.Equal("home_no_destination", destinations[GameFamily.Lgpe]); // o Let's Go não recebe do HOME
        Assert.Equal("home_no_destination", destinations[GameFamily.Frlg]); // não volta para a Gen 3

        var sprigatito = await LegalAsync(TradeGame.Scarlet, 906, 10);
        Assert.Equal("species_not_in_game", HomeTransfer.Destinations(sprigatito, GameFamily.Sv)[GameFamily.Swsh]);
    }

    [Fact]
    public async Task Do_firered_so_sem_item_e_sem_hm()
    {
        var pk = await LegalAsync(TradeGame.FireRed, 25, 10);
        pk.HeldItem = 1;
        Assert.Equal("frlg_held_item", HomeTransfer.Move(pk, GameCatalog.Get(GameFamily.Sv), Owner).Refusal);
        pk.HeldItem = 0;
        pk.Move1 = 148; // Flash
        Assert.Equal("frlg_hm_move", HomeTransfer.Move(pk, GameCatalog.Get(GameFamily.Sv), Owner).Refusal);
    }
}
