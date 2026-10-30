using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Activities;
using TradeRock.Core.Engines.Pokeldn;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Tests;

/// <summary>Outras atividades (D26): o catálogo de presentes, a API e o pedido ao pokeldn.</summary>
public sealed class ActivityTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-activity-").FullName;
    private readonly FakePokeldn _pokeldn = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _app = TradeRockServer.Create(["--data-dir", _dataDir, "--Engines:Fake:SpeedFactor=0"], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<IPokeldnProcess>(_ => _pokeldn);
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("http://127.0.0.1/");
        _client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        try { Directory.Delete(_dataDir, true); } catch (IOException) { }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> Error(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors")[0].GetString()!;
    }

    private async Task<JsonElement> FinishedAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            var current = await Json(await _client.GetAsync("/api/activities/current"));
            if (current.GetProperty("status").GetString() != "running")
                return current;
            await Task.Delay(20);
        }
        throw new TimeoutException();
    }

    [Fact]
    public void Catalogo_de_presentes_embutido()
    {
        var frlg = GiftCatalog.Embedded.Games[GameFamily.Frlg];
        Assert.Contains(frlg.Presets, p => p.Key == "celebi");
        Assert.Empty(frlg.Events);
        // Pokémon de evento (pedem o ajudante PKHeX do pokeldn) e leitura do save ficam de fora.
        Assert.DoesNotContain(frlg.Presets, p => p.Key.StartsWith("event-") || p.Group == "Read the save");
        var swsh = GiftCatalog.Embedded.Games[GameFamily.Swsh];
        Assert.Contains(swsh.Events, e => e.Key == "pokemon-go-transfer-gift-melmetal");
        Assert.NotEmpty(swsh.Presets);

        Assert.Null(GiftCatalog.FileProblem(GameFamily.Swsh, "wc8", new byte[0x2D0]));
        Assert.Equal("gift_file_invalid", GiftCatalog.FileProblem(GameFamily.Swsh, "wc8", new byte[100]));
        Assert.Equal("gift_file_wrong_type", GiftCatalog.FileProblem(GameFamily.Swsh, "wc3", new byte[0x58C]));
        Assert.Null(GiftCatalog.FileProblem(GameFamily.Frlg, "wc3", new byte[0x58C]));
        Assert.Equal("gift_file_other_game", GiftCatalog.FileProblem(GameFamily.Frlg, "pokegift", Encoding.UTF8.GetBytes("""{"game": "swsh"}""")));
        Assert.Null(GiftCatalog.FileProblem(GameFamily.Frlg, "pokegift", Encoding.UTF8.GetBytes("""{"game": "frlg"}""")));
    }

    [Fact]
    public async Task Mystery_gift_simulado_pela_api()
    {
        var catalog = await Json(await _client.GetAsync("/api/activities"));
        Assert.True(catalog.GetProperty("engineSupports").GetBoolean());
        Assert.Equal(["frlg", "swsh"], catalog.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("game").GetString()).Order());

        var started = await Json(await _client.PostAsJsonAsync("/api/activities",
            new { game = "leaf_green", kind = "gift", gift = new { mode = "preset", key = "celebi" } }));
        Assert.Equal("Celebi", started.GetProperty("giftLabel").GetString());
        var done = await FinishedAsync();
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Contains(done.GetProperty("events").EnumerateArray(), e => e.GetProperty("kind").GetString() == "delivered");

        // Presente que não existe, evento no FRLG, arquivo errado, jogo sem Mystery Gift local.
        Assert.Equal("gift_unknown", await Error(await _client.PostAsJsonAsync("/api/activities",
            new { game = "fire_red", kind = "gift", gift = new { mode = "preset", key = "event-aura-mew" } })));
        Assert.Equal("gift_unknown", await Error(await _client.PostAsJsonAsync("/api/activities",
            new { game = "fire_red", kind = "gift", gift = new { mode = "event", key = "pokemon-go-transfer-gift-melmetal" } })));
        Assert.Equal("gift_file_invalid", await Error(await _client.PostAsJsonAsync("/api/activities",
            new { game = "sword", kind = "gift", gift = new { mode = "file", data = Convert.ToBase64String(new byte[10]), extension = ".wc8" } })));
        Assert.Equal("activity_game_unsupported", await Error(await _client.PostAsJsonAsync("/api/activities",
            new { game = "scarlet", kind = "gift", gift = new { mode = "preset", key = "celebi" } })));
    }

    [Fact]
    public async Task Presente_do_sword_fica_no_ar_ate_encerrar_e_bloqueia_a_troca()
    {
        await Json(await _client.PostAsJsonAsync("/api/activities", new
        {
            game = "sword", kind = "gift",
            gift = new { mode = "file", data = Convert.ToBase64String(new byte[0x2D0]), extension = "wc8", fileName = "cartao.wc8" },
        }));
        // Com o rádio ocupado, nem outra atividade nem uma troca começam.
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/activities",
            new { game = "sword", kind = "gift", gift = new { mode = "preset", key = "pikachu" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/trades", new
        {
            party = Array.Empty<Guid>(), offeredSlots = Array.Empty<int>(), game = "fire_red", trainer = new { name = "Ana", tid = 1, sid = 2 },
        })).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/activities/current/cancel", null)).StatusCode);
        var done = await FinishedAsync();
        Assert.Equal("completed", done.GetProperty("status").GetString()); // o console só ouve o anúncio
        Assert.Equal("cartao.wc8", done.GetProperty("giftLabel").GetString());
    }

    [Fact]
    public async Task Esp32_manda_o_presente_ao_pokeldn()
    {
        var keys = "aes_kek_generation_source = 00112233445566778899aabbccddeeff\naes_key_generation_source = 00112233445566778899aabbccddeeff\nmaster_key_00 = 00112233445566778899aabbccddeeff\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(keys)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", serialPort = "/dev/ttyUSB0" }));
        var engine = (IActivityEngine)_app.Services.GetServices<ITradeEngine>().Single(e => e.Id == "esp32");

        _pokeldn.Script = [Message("event", "starting"), Message("event", "waiting"), Message("event", "joined"),
            new JsonObject { ["type"] = "done", ["kind"] = "delivery", ["index"] = 1 }, new JsonObject { ["type"] = "result", ["exitCode"] = 0 }];
        var events = new List<ActivityEvent>();
        var request = new ActivityRequest(TradeGame.LeafGreen, ActivityKind.Gift, new TrainerProfile("ROCHA", 12345, 54321),
            new GiftChoice("preset", "master-ball"));
        var result = await engine.RunActivityAsync(request, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal([ActivityEventKind.Started, ActivityEventKind.Waiting, ActivityEventKind.Joined, ActivityEventKind.Delivered],
            events.Select(e => e.Kind));
        var sent = _pokeldn.Last!;
        Assert.Equal(("gift", "frlg", "leafgreen"), (sent["activity"]!.GetValue<string>(), sent["game"]!.GetValue<string>(), sent["version"]!.GetValue<string>()));
        Assert.Equal("master-ball", sent["gift"]!["preset"]!.GetValue<string>());
        Assert.Equal("esp32:/dev/ttyUSB0", sent["radio"]!.GetValue<string>());
        Assert.Equal(12345u, sent["trainer"]!["tid"]!.GetValue<uint>());

        // FRLG sem a entrega confirmada não deu certo; Sword com o anúncio no ar, sim.
        _pokeldn.Script = [Message("event", "starting"), new JsonObject { ["type"] = "result", ["exitCode"] = 0 }];
        Assert.Equal("gift_not_received", (await engine.RunActivityAsync(request, _ => ValueTask.CompletedTask, CancellationToken.None)).Code);
        _pokeldn.Script = [Message("event", "starting"), new JsonObject { ["type"] = "milestone", ["kind"] = "advertising" }];
        var sword = request with { Game = TradeGame.Sword, Gift = new GiftChoice("event", "pokemon-go-transfer-gift-melmetal") };
        Assert.True((await engine.RunActivityAsync(sword, _ => ValueTask.CompletedTask, CancellationToken.None)).Ok);
        Assert.Equal(PokeldnActivity.GiftSeconds, _pokeldn.Last!["seconds"]!.GetValue<int>());
        Assert.Equal("pokemon-go-transfer-gift-melmetal", _pokeldn.Last!["gift"]!["event"]!.GetValue<string>());
    }

    private static JsonObject Message(string type, string kind) => new() { ["type"] = type, ["kind"] = kind };
}
