using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Saves;

namespace TradeRock.Tests;

public sealed class VaultApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private WebApplication _app = null!;
    private string _dataDir = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("traderock-vault-").FullName;
        _app = TradeRockServer.Create(["--data-dir", _dataDir, "--Engines:Fake:SpeedFactor=0"], _token, b => b.WebHost.UseTestServer());
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("http://127.0.0.1/");
        _client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<JsonElement> Get(string url) => await Json(await _client.GetAsync(url));
    private async Task<JsonElement> Post(string url, object body) => await Json(await _client.PostAsJsonAsync(url, body));

    private async Task<Guid[]> DemoIds() =>
        (await Get("/api/vault/pokemon")).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToArray();

    [Fact]
    public async Task Caixas_mover_buscar_e_lixeira()
    {
        var ids = await DemoIds();
        Assert.Equal(2, (await Get("/api/vault")).GetProperty("inboxCount").GetInt32());

        var box = await Post("/api/vault/boxes", new { name = "Lendários" });
        var boxId = box.GetProperty("id").GetGuid();

        await Post($"/api/vault/pokemon/{ids[0]}/move", new { boxId, slot = 7 });
        var inBox = await Get($"/api/vault/pokemon?scope={boxId}");
        Assert.Equal(7, Assert.Single(inBox.EnumerateArray()).GetProperty("slot").GetInt32());

        var overview = await Get("/api/vault");
        Assert.Equal(1, overview.GetProperty("inboxCount").GetInt32());
        Assert.Equal(1, overview.GetProperty("boxes")[0].GetProperty("count").GetInt32());

        Assert.Single((await Get("/api/vault/pokemon?text=deox")).EnumerateArray());
        Assert.Equal(2, (await Get("/api/vault/pokemon?legality=legal")).GetArrayLength());

        var full = await _client.PostAsJsonAsync($"/api/vault/pokemon/{ids[1]}/move", new { boxId, slot = 99 });
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        Assert.Contains("slot_invalid", await full.Content.ReadAsStringAsync());

        await Post("/api/vault/trash", new { ids = new[] { ids[0] } });
        Assert.Equal(1, (await Get("/api/vault")).GetProperty("trashCount").GetInt32());
        Assert.Single((await Get("/api/vault/pokemon?scope=trash")).EnumerateArray());

        // Da lixeira não dá para mandar numa troca.
        var trade = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = ids,
            offeredSlots = new[] { 1 },
            game = "fire_red",
            trainer = new { name = "EMU", tid = 1 },
        });
        Assert.Equal(HttpStatusCode.BadRequest, trade.StatusCode);

        await Post("/api/vault/restore", new { ids = new[] { ids[0] } });
        var emptied = await Post("/api/vault/trash/empty", new { });
        Assert.Equal(0, emptied.GetProperty("deleted").GetInt32());
    }

    [Fact]
    public async Task Tags_filtros_e_duplicatas()
    {
        var ids = await DemoIds();
        var tag = await Post("/api/vault/tags", new { name = "Para trocar", color = "sky" });
        var tagId = tag.GetProperty("id").GetGuid();

        await Post("/api/vault/tags/assign", new { pokemonIds = ids, add = new[] { tagId } });
        var tagged = await Get($"/api/vault/pokemon?tags={tagId}");
        Assert.Equal(2, tagged.GetArrayLength());
        Assert.All(tagged.EnumerateArray(), p => Assert.Contains(tagId, p.GetProperty("tagIds").EnumerateArray().Select(t => t.GetGuid())));

        var bad = await _client.PostAsJsonAsync("/api/vault/tags", new { name = "Para trocar", color = "sky" });
        Assert.Contains("tag_exists", await bad.Content.ReadAsStringAsync());

        Assert.Empty((await Get("/api/vault/pokemon?duplicates=true")).EnumerateArray());
        // Deoxys (#386) sem "Meu treinador": aviso de Pokédex Nacional.
        Assert.Single((await Get("/api/vault/pokemon?warnings=true")).EnumerateArray());
    }

    [Fact]
    public async Task Exportar_um_e_varios()
    {
        var ids = await DemoIds();

        var ek3 = await _client.GetByteArrayAsync($"/api/pokemon/{ids[0]}/file?format=ek3&size=80");
        Assert.Equal(80, ek3.Length);
        Assert.InRange(Pk3Reader.Read(ek3).Summary.Species, 1, 386);

        var zipResponse = await _client.PostAsJsonAsync("/api/vault/export", new { ids, format = "pk3", size = 100 });
        Assert.Equal("application/zip", zipResponse.Content.Headers.ContentType?.MediaType);
        using var zip = new ZipArchive(await zipResponse.Content.ReadAsStreamAsync());
        Assert.Equal(2, zip.Entries.Count);
        Assert.All(zip.Entries, e => Assert.EndsWith(".pk3", e.Name));

        var invalid = await _client.PostAsJsonAsync("/api/vault/export", new { ids, format = "pk9", size = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // Sem janela nativa, a exportação por diálogo do sistema não se aplica.
        var native = await _client.PostAsJsonAsync("/api/vault/export/native", new { ids, format = "pk3", size = 100 });
        Assert.Equal(HttpStatusCode.Conflict, native.StatusCode);
    }

    [Fact]
    public async Task Injetar_no_save_pelo_navegador_com_backup()
    {
        var ids = await DemoIds();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(SaveFixture.BlankFireRed()), "file", "FireRed.sav" } };
        var opened = await Json(await _client.PostAsync("/api/saves/upload", form));
        var token = opened.GetProperty("token").GetGuid();
        Assert.False(opened.GetProperty("inPlace").GetBoolean());
        Assert.Empty(opened.GetProperty("layout").GetProperty("occupied").EnumerateArray());

        var injected = await Post($"/api/saves/{token}/inject", new
        {
            placements = new[] { new { pokemonId = ids[0], box = 0, slot = 0 }, new { pokemonId = ids[1], box = 0, slot = 1 } },
        });
        Assert.True(injected.GetProperty("download").GetBoolean());
        var backup = injected.GetProperty("backup").GetString()!;
        Assert.True(File.Exists(backup));
        Assert.StartsWith(Path.Combine(_dataDir, "backups"), backup);

        Assert.Equal(2, injected.GetProperty("save").GetProperty("layout").GetProperty("occupied").GetArrayLength());

        var downloaded = await _client.GetByteArrayAsync($"/api/saves/{token}/download");
        var layout = SaveInjection.Describe(downloaded);
        Assert.Equal(2, layout.Occupied.Count);

        // A mesma sessão já enxerga o save novo: a posição 0 está ocupada.
        var again = await _client.PostAsJsonAsync($"/api/saves/{token}/inject", new
        {
            placements = new[] { new { pokemonId = ids[0], box = 0, slot = 0 } },
        });
        Assert.Contains("slot_occupied", await again.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync("/api/saves/open", null)).StatusCode); // sem janela
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/saves/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Importar_do_save_inteiro()
    {
        var mewtwo = Pk3Reader.Read(DemoPokemon.All.Single(d => d.Name == "mewtwo.pk3").Data).Data;
        var save = SaveInjection.Inject(SaveFixture.BlankFireRed(), [new(mewtwo, 2, 3)]);

        using var form = new MultipartFormDataContent { { new ByteArrayContent(save), "file", "FireRed.sav" } };
        var read = await Json(await _client.PostAsync("/api/pokemon/sav", form));
        Assert.Equal("RED", read.GetProperty("trainerName").GetString());
        var entry = Assert.Single(read.GetProperty("entries").EnumerateArray());

        var imported = await Post($"/api/pokemon/sav/{read.GetProperty("token").GetGuid()}/import",
            new { indices = new[] { entry.GetProperty("index").GetInt32() } });
        Assert.False(imported[0].GetProperty("created").GetBoolean()); // já era o Mewtwo de demonstração
    }
}
