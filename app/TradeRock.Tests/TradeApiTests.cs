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
using TradeRock.Core.Pokemon;

namespace TradeRock.Tests;

/// <summary>Ponta a ponta pela API, com o FakeEngine em velocidade máxima.</summary>
public sealed class TradeApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private WebApplication _app = null!;
    private string _dataDir = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("traderock-api-").FullName;
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

    [Fact]
    public async Task Primeira_execucao_traz_os_pokemon_de_demonstracao()
    {
        var list = await GetJsonAsync("/api/pokemon");
        var names = list.EnumerateArray().Select(p => p.GetProperty("speciesName").GetString()).Order();
        Assert.Equal(["Deoxys", "Mewtwo"], names);
        Assert.All(list.EnumerateArray(), p => Assert.Equal("demo", p.GetProperty("origin").GetString()));
    }

    [Fact]
    public async Task Troca_completa_pela_api_com_progresso_ao_vivo()
    {
        var party = await DemoPartyAsync();

        var start = await _client.PostAsJsonAsync("/api/trades", new
        {
            party,
            offeredSlots = new[] { 1 },
            game = "fire_red",
            trainer = new { name = "Rocha", tid = 12345, sid = 54321 },
            simulation = new { scenario = "success" },
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        var events = await ReadEventsUntilFinishedAsync();
        var kinds = events.Select(e => e.GetProperty("kind").GetString()).ToList();

        Assert.Equal("session_started", kinds[0]);
        Assert.Contains("join_requested", kinds);
        Assert.Contains("trade_done", kinds);
        Assert.Equal("session_finished", kinds[^1]);
        Assert.Equal("completed", events[^1].GetProperty("status").GetString());
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "join_requested"
            && e.GetProperty("message").GetString()!.Contains("Rocha"));

        var done = events.Single(e => e.GetProperty("kind").GetString() == "trade_done");
        var receivedId = done.GetProperty("receivedPokemonId").GetGuid();
        Assert.False(done.TryGetProperty("receivedData", out _)); // bytes nunca vão para o cliente

        // O recebido está no cofre e pode ser baixado como .pk3 válido.
        var file = await _client.GetByteArrayAsync($"/api/pokemon/{receivedId}/file");
        Assert.InRange(Pk3Reader.Read(file).Summary.Species, 1, 386);
        // O Switch simulado manda um Pokémon diferente do oferecido.
        Assert.NotEqual(party[1], receivedId);

        // Histórico
        var history = await GetJsonAsync("/api/trades");
        var item = Assert.Single(history.EnumerateArray());
        Assert.Equal("completed", item.GetProperty("status").GetString());
        Assert.Equal("fire_red", item.GetProperty("game").GetString());
        Assert.Equal("Rocha", item.GetProperty("trainerName").GetString());
        Assert.True(item.GetProperty("isSimulation").GetBoolean());
        var entry = Assert.Single(item.GetProperty("entries").EnumerateArray());
        Assert.Equal(party[1], entry.GetProperty("sentPokemonId").GetGuid());
        Assert.Equal(receivedId, entry.GetProperty("receivedPokemonId").GetGuid());

        var detail = await GetJsonAsync($"/api/trades/{item.GetProperty("id").GetGuid()}");
        Assert.True(detail.GetProperty("events").GetArrayLength() >= kinds.Count - 1);
    }

    [Fact]
    public async Task Segunda_troca_simultanea_e_recusada_e_cancelar_funciona()
    {
        var party = await DemoPartyAsync();
        var body = new
        {
            party,
            offeredSlots = new[] { 0 },
            game = "leaf_green",
            trainer = new { name = "EMU", tid = 1 },
            simulation = new { scenario = "approval_never_given" },
        };

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsJsonAsync("/api/trades", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/trades", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PutAsJsonAsync("/api/engines/selected", new { id = "fake" })).StatusCode);

        var cancel = await _client.PostAsync("/api/trades/current/cancel", null);
        var snapshot = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("cancelled", snapshot.GetProperty("status").GetString());

        var history = await GetJsonAsync("/api/trades");
        Assert.Equal("cancelled", history[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pedido_invalido_explica_os_erros()
    {
        var party = await DemoPartyAsync();
        var response = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { party[0] },
            offeredSlots = new[] { 0 },
            game = "fire_red",
            trainer = new { name = "Conceição", tid = 1 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("party_size", errors);
        Assert.Contains("trainer_name_too_long", errors);
        Assert.Contains("trainer_name_invalid_chars", errors);
    }

    [Fact]
    public async Task Pokemon_desconhecido_e_recusado()
    {
        var response = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { Guid.NewGuid(), Guid.NewGuid() },
            offeredSlots = new[] { 0 },
            game = "fire_red",
            trainer = new { name = "EMU", tid = 1 },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_de_pk3_ek3_e_arquivo_invalido()
    {
        // .ek3 (criptografado) do Mewtwo de demonstração: mesmo hash, não duplica.
        var plain = Pk3Reader.Read(DemoPokemon.All.Single(d => d.Name == "mewtwo.pk3").Data);
        var pk = new PK3(plain.Data.ToArray());
        var ek3 = new byte[plain.Data.Length];
        if (plain.HasPartyData) pk.WriteEncryptedDataParty(ek3); else pk.WriteEncryptedDataStored(ek3);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(ek3), "files", "mewtwo.ek3" },
            { new ByteArrayContent(new byte[42]), "files", "lixo.pk3" },
        };
        var response = await _client.PostAsync("/api/pokemon", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(results[0].GetProperty("ok").GetBoolean());
        Assert.False(results[0].GetProperty("created").GetBoolean());
        Assert.False(results[1].GetProperty("ok").GetBoolean());
        Assert.Equal("size", results[1].GetProperty("error").GetString());

        Assert.Equal(2, (await GetJsonAsync("/api/pokemon")).GetArrayLength());
    }

    [Fact]
    public async Task Configuracoes_de_troca_sao_normalizadas_e_guardadas()
    {
        var put = await _client.PutAsJsonAsync("/api/settings/trade", new { maxAttempts = 99, retryDelaySeconds = 2 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var settings = await GetJsonAsync("/api/settings/trade");
        Assert.Equal(20, settings.GetProperty("maxAttempts").GetInt32());
        Assert.Equal(2, settings.GetProperty("retryDelaySeconds").GetDouble());
    }

    [Fact]
    public async Task Motores_listam_o_fake_como_simulacao()
    {
        var engines = await GetJsonAsync("/api/engines");
        var fake = Assert.Single(engines.EnumerateArray());
        Assert.Equal("fake", fake.GetProperty("id").GetString());
        Assert.True(fake.GetProperty("isSimulation").GetBoolean());
        Assert.True(fake.GetProperty("selected").GetBoolean());
    }

    [Fact]
    public async Task Presets_de_treinador()
    {
        var bad = await _client.PostAsJsonAsync("/api/trainer-presets",
            new { label = "Meu", name = "Joãozinho", tid = 1, game = "fire_red" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await _client.PostAsJsonAsync("/api/trainer-presets",
            new { label = "Principal", name = "ROCHA", tid = 4242, sid = 17, game = "leaf_green" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var id = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        Assert.Single((await GetJsonAsync("/api/trainer-presets")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/trainer-presets/{id}")).StatusCode);
        Assert.Empty((await GetJsonAsync("/api/trainer-presets")).EnumerateArray());
    }

    [Fact]
    public async Task Sprites_das_especies()
    {
        var ok = await _client.GetAsync("/api/sprites/150");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/png", ok.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/sprites/0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/sprites/387")).StatusCode);
    }

    [Fact]
    public async Task Eventos_exigem_sessao()
    {
        using var anonymous = _app.GetTestClient();
        anonymous.BaseAddress = new Uri("http://127.0.0.1/");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/trades/events")).StatusCode);
    }

    private async Task<Guid[]> DemoPartyAsync()
    {
        var list = await GetJsonAsync("/api/pokemon");
        return list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToArray();
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>Lê o stream SSE até o evento session_finished.</summary>
    private async Task<List<JsonElement>> ReadEventsUntilFinishedAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/trades/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var events = new List<JsonElement>();
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            var e = JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone();
            events.Add(e);
            if (e.GetProperty("kind").GetString() == "session_finished")
                break;
        }
        return events;
    }
}
