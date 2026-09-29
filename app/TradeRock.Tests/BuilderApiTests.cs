using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using TradeRock.App;
using TradeRock.Core;

namespace TradeRock.Tests;

public sealed class BuilderApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private WebApplication _app = null!;
    private string _dataDir = null!;
    private HttpClient _client = null!;

    private static readonly object Trainer = new { name = "ROCHA", tid = 24680, sid = 13579, gender = 0 };

    public async ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("traderock-builder-").FullName;
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

    private async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<JsonElement> Get(string url) => await Json(await _client.GetAsync(url));
    private async Task<JsonElement> Post(string url, object body) => await Json(await _client.PostAsJsonAsync(url, body));

    private async Task<Guid> GenerateAndSave(object spec)
    {
        var generated = await Post("/api/builder/generate", new { spec, language = "pt-BR" });
        var saved = await Post("/api/builder/save", new { data = generated.GetProperty("data").GetString() });
        return saved.GetProperty("pokemon").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Dados_do_formulario()
    {
        var data = await Get("/api/builder/data");
        Assert.Equal(386, data.GetProperty("species").GetArrayLength());
        Assert.Equal(354, data.GetProperty("moves").GetArrayLength());
        Assert.Contains(data.GetProperty("items").EnumerateArray(), i => i.GetProperty("text").GetString() == "Leftovers");
        Assert.Equal(12, data.GetProperty("balls").GetArrayLength());

        var snorlax = await Get("/api/builder/species/143");
        Assert.Equal(2, snorlax.GetProperty("abilities").GetArrayLength());

        var hooh = await Get("/api/builder/species/250/encounters?game=fire_red");
        Assert.Equal(174, hooh[0].GetProperty("location").GetInt32()); // Navel Rock primeiro

        Assert.NotEmpty((await Get("/api/builder/locations?version=FR")).EnumerateArray());
    }

    [Fact]
    public async Task Gerar_salvar_e_reabrir_no_construtor()
    {
        var generated = await Post("/api/builder/generate", new
        {
            spec = new { species = 150, level = 70, nature = 15, trainer = Trainer, game = "fire_red" },
            language = "pt-BR",
        });
        Assert.Equal("legal", generated.GetProperty("legality").GetProperty("status").GetString());
        Assert.Equal("regenerated", generated.GetProperty("generation").GetString());

        var saved = await Post("/api/builder/save", new { data = generated.GetProperty("data").GetString() });
        var pokemon = saved.GetProperty("pokemon");
        Assert.Equal("created", pokemon.GetProperty("origin").GetString());
        Assert.Equal("legal", pokemon.GetProperty("legality").GetString());

        var id = pokemon.GetProperty("id").GetGuid();
        var reopened = await Get($"/api/builder/from/{id}");
        Assert.Equal(150, reopened.GetProperty("spec").GetProperty("species").GetInt32());
        Assert.Equal(15, reopened.GetProperty("spec").GetProperty("nature").GetInt32());
        Assert.Equal("ROCHA", reopened.GetProperty("raw").GetProperty("otName").GetString());

        var report = await Get($"/api/pokemon/{id}/legality?lang=en");
        Assert.Equal("legal", report.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pokemon_ilegal_so_troca_no_modo_avancado_com_confirmacao()
    {
        // Mewtwo legal, depois com o PID alterado no modo avançado.
        var legalId = await GenerateAndSave(new { species = 150, level = 70, trainer = Trainer });
        var raw = (await Get($"/api/builder/from/{legalId}")).GetProperty("raw");
        var rawObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw.GetRawText())!;
        rawObj["pid"] = JsonSerializer.SerializeToElement(raw.GetProperty("pid").GetUInt32() ^ 0x00010001u);

        var advanced = await Post("/api/builder/advanced", new { raw = rawObj, language = "pt-BR" });
        Assert.Equal("illegal", advanced.GetProperty("legality").GetProperty("status").GetString());
        var illegalId = (await Post("/api/builder/save", new { data = advanced.GetProperty("data").GetString() }))
            .GetProperty("pokemon").GetProperty("id").GetGuid();

        object Trade(bool accept) => new
        {
            party = new[] { legalId, illegalId },
            offeredSlots = new[] { 1 },
            game = "fire_red",
            trainer = new { name = "EMU", tid = 1 },
            acceptIllegal = accept,
        };

        var blocked = await _client.PostAsJsonAsync("/api/trades", Trade(accept: true));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("illegal_pokemon", await blocked.Content.ReadAsStringAsync());

        await Json(await _client.PutAsJsonAsync("/api/settings/trade", new { allowIllegal = true, maxAttempts = 5 }));

        var needsConfirmation = await _client.PostAsJsonAsync("/api/trades", Trade(accept: false));
        Assert.Contains("illegal_needs_confirmation", await needsConfirmation.Content.ReadAsStringAsync());

        var accepted = await _client.PostAsJsonAsync("/api/trades", Trade(accept: true));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        // O ilegal pode ficar no time como "preenchimento" sem bloquear.
        await _client.PostAsync("/api/trades/current/cancel", null);
        await Json(await _client.PutAsJsonAsync("/api/settings/trade", new { allowIllegal = false, maxAttempts = 5 }));
        var asFiller = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { legalId, illegalId },
            offeredSlots = new[] { 0 },
            game = "fire_red",
            trainer = new { name = "EMU", tid = 1 },
        });
        Assert.Equal(HttpStatusCode.Accepted, asFiller.StatusCode);
    }

    [Fact]
    public async Task Meu_treinador_vira_ot_padrao_e_gera_avisos()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/settings/my-trainer")).StatusCode);
        Assert.Equal("EMU", (await Get("/api/builder/default-trainer")).GetProperty("name").GetString());

        var bad = await _client.PutAsJsonAsync("/api/settings/my-trainer", new { name = "João", tid = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        await Json(await _client.PutAsJsonAsync("/api/settings/my-trainer",
            new { name = "ROCHA", tid = 24680, sid = 13579, badges = 3, hasNationalDex = false }));
        Assert.Equal("ROCHA", (await Get("/api/builder/default-trainer")).GetProperty("name").GetString());

        // Mewtwo nível 70 de outro treinador com 3 insígnias: desobedece.
        var other = await GenerateAndSave(new { species = 150, level = 70, trainer = new { name = "OUTRO", tid = 1, sid = 2 } });
        var dto = await Get($"/api/pokemon/{other}");
        Assert.Contains(dto.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "obedience");

        // Deoxys (#386) sem Pokédex Nacional.
        var deoxys = await GenerateAndSave(new { species = 386, level = 30, trainer = Trainer });
        var deoxysDto = await Get($"/api/pokemon/{deoxys}");
        Assert.Contains(deoxysDto.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "national_dex_required");
        Assert.True(deoxysDto.GetProperty("fateful").GetBoolean());

        // Descobrir o SID a partir de um Pokémon capturado pelo jogador.
        var fromMon = await Post($"/api/settings/my-trainer/from-pokemon/{other}", new { });
        Assert.Equal("OUTRO", fromMon.GetProperty("name").GetString());
        Assert.Equal(2, fromMon.GetProperty("sid").GetInt32());
        Assert.Equal(3, fromMon.GetProperty("badges").GetInt32()); // mantém o que já estava configurado
    }

    [Fact]
    public async Task Importar_texto_do_showdown()
    {
        var items = await Post("/api/builder/showdown", new { text = "Snorlax @ Leftovers\nLevel: 50\nCareful Nature\n- Rest\n- Body Slam", game = "fire_red" });
        var spec = items[0].GetProperty("spec");
        Assert.Equal(143, spec.GetProperty("species").GetInt32());
        Assert.Equal("EMU", spec.GetProperty("trainer").GetProperty("name").GetString());

        var empty = await _client.PostAsJsonAsync("/api/builder/showdown", new { text = " " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_save_e_recusado()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[0x20000]), "file", "lixo.sav" } };
        var response = await _client.PostAsync("/api/pokemon/sav", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sav_unknown", await response.Content.ReadAsStringAsync());

        var expired = await _client.PostAsJsonAsync($"/api/pokemon/sav/{Guid.NewGuid()}/import", new { indices = new[] { 0 } });
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
    }

    [Fact]
    public async Task Demonstracao_e_recebidos_tem_legalidade()
    {
        // A análise de quem já estava no cofre roda em segundo plano na inicialização.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        JsonElement list;
        do
        {
            list = await Get("/api/pokemon");
            if (list.EnumerateArray().All(p => p.GetProperty("legality").ValueKind == JsonValueKind.String))
                break;
            await Task.Delay(100);
        } while (DateTime.UtcNow < deadline);

        Assert.All(list.EnumerateArray(), p => Assert.NotEqual(JsonValueKind.Null, p.GetProperty("legality").ValueKind));
    }
}
