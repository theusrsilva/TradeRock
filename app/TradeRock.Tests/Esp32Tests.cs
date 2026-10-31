using System.Diagnostics;
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
using TradeRock.Core.Engines;
using TradeRock.Core.Engines.Pokeldn;
using TradeRock.Core.Esp32;
using TradeRock.Core.Setup;
using TradeRock.Core.Trading;

namespace TradeRock.Tests;

internal static class Repo
{
    public static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "TradeRock.slnx")))
                dir = Path.GetDirectoryName(dir)!;
            return dir;
        }
    }
}

public class Esp32FirmwareTests
{
    [Fact]
    public void Binarios_embutidos_conferem_e_batem_com_o_catalogo()
    {
        var hashes = Esp32Firmware.PublishedHashes();
        Assert.Equal(4, hashes.Count);
        foreach (var image in Esp32Firmware.Images)
        {
            var data = Esp32Firmware.Read(image);
            // Cabeçalho de imagem ESP do bootloader: em 0x1000 no ESP32 clássico, em 0x0 nos outros
            // (firmware/release/README.md, merge-bin); o binário completo é gravado em 0x0.
            Assert.Equal(0xe9, data[image.Target == "esp32" ? 0x1000 : 0]);
            Assert.Contains(image.File, hashes.Keys);
        }
        // Todo chip do catálogo que o firmware aceita tem um binário.
        foreach (var board in HardwareCatalog.Embedded.Esp32.Where(b => b.Status != CompatStatus.Unsupported))
            Assert.NotNull(Esp32Firmware.For(board.Chip));
    }

    [Fact]
    public void Extrair_grava_o_binario_conferido()
    {
        var dir = Directory.CreateTempSubdirectory("traderock-fw-").FullName;
        var image = Esp32Firmware.For("esp32s3")!;
        var path = Esp32Firmware.ExtractTo(image, dir);
        Assert.Equal(Esp32Firmware.Read(image), File.ReadAllBytes(path));
        Directory.Delete(dir, true);
    }
}

file sealed class NoopLink : IEsp32Link
{
    public Esp32Hello Probe(string port, CancellationToken ct) => new("esp32s3");
    public Esp32ScanResult Scan(string port, string keysPath, TimeSpan limit, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Do assistente (simulação linux-esp32) até a troca pela API, com o pokeldn falso.</summary>
public sealed class Esp32ApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-esp32api-").FullName;
    private readonly FakePokeldn _pokeldn = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _app = TradeRockServer.Create(["--data-dir", _dataDir, "--environment", "Development"], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<IEsp32Link, NoopLink>();
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

    private async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Check(JsonElement status, string id) =>
        status.GetProperty("checks").EnumerateArray().First(c => c.GetProperty("id").GetString() == id).GetProperty("code").GetString()!;

    private async Task<JsonElement> WaitFlashAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            var job = await Json(await _client.GetAsync("/api/setup/esp32/flash"));
            if (job.GetProperty("job").GetProperty("stage").GetString() is "done" or "error")
                return job;
            await Task.Delay(50);
        }
        throw new TimeoutException();
    }

    [Fact]
    public async Task Firmware_do_assistente_simulado()
    {
        await Json(await _client.PutAsJsonAsync("/api/setup/simulation", new { scenario = "linux-esp32" }));
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", esp32Board = "esp32-s3", serialPort = "/dev/ttyUSB0" }));

        var probe = await Json(await _client.PostAsync("/api/setup/esp32/probe", null));
        Assert.Equal("esp32_firmware_missing", probe.GetProperty("result").GetProperty("code").GetString());
        Assert.Equal("esp32_probe_esp32_firmware_missing", Check(probe.GetProperty("status"), "esp32_firmware"));

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/setup/esp32/flash", null)).StatusCode);
        var job = await WaitFlashAsync();
        Assert.Equal("done", job.GetProperty("job").GetProperty("stage").GetString());
        var status = job.GetProperty("status");
        Assert.Equal("esp32_firmware_ok", Check(status, "esp32_firmware"));
        Assert.Equal("esp32_chip_ok", Check(status, "esp32_chip"));
        Assert.Equal("esp32_firmware_verified", Check(status, "esp32_firmware_version"));

        // Teste com o Switch pelo ESP32 (simulado): o resultado vale para a porta e o chip.
        var keys = "aes_kek_generation_source = 00112233445566778899aabbccddeeff\naes_key_generation_source = 00112233445566778899aabbccddeeff\nmaster_key_00 = 00112233445566778899aabbccddeeff\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(keys)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
        var test = await Json(await _client.PostAsJsonAsync("/api/setup/esp32/switch-test", new { simulatedOutcome = "found_decrypted" }));
        Assert.Equal("esp32", test.GetProperty("result").GetProperty("radio").GetString());
        Assert.Equal("switch_test_ok", Check(test.GetProperty("status"), "switch_test"));

        // Placa escolhida diferente do chip: com o chip identificado, o aviso vem uma vez só (pelo chip).
        var other = await Json(await _client.PutAsJsonAsync("/api/setup/state", new { esp32Board = "esp32-c3" }));
        Assert.Equal("esp32_board_mismatch", Check(other, "esp32_chip"));
        Assert.Equal("esp32_firmware_ok", Check(other, "esp32_firmware"));
    }

    private async Task<object> TradeBodyAsync(int trades)
    {
        var list = await Json(await _client.GetAsync("/api/pokemon"));
        var party = list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToArray();
        return new { party, offeredSlots = Enumerable.Range(0, trades).Reverse().ToArray(), game = "fire_red", trainer = new { name = "ROCHA", tid = 12345, sid = 54321 } };
    }

    [Fact]
    public async Task Motor_esp32_passa_o_time_o_treinador_e_as_chaves_ao_pokeldn()
    {
        var keys = "aes_kek_generation_source = 00112233445566778899aabbccddeeff\naes_key_generation_source = 00112233445566778899aabbccddeeff\nmaster_key_00 = 00112233445566778899aabbccddeeff\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(keys)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", serialPort = "/dev/ttyUSB0" }));

        var engine = _app.Services.GetServices<ITradeEngine>().Single(e => e.Id == "esp32");
        Assert.True((await engine.CheckAvailabilityAsync(CancellationToken.None)).Available);
        Assert.True(engine.Supports(TradeRock.Core.Games.GameFamily.Sv));

        var events = new List<TradeEvent>();
        var demo = TradeRock.Core.Pokemon.DemoPokemon.All;
        var party = new List<PartyMember> { new(Guid.NewGuid(), demo[0].Data, "a"), new(Guid.NewGuid(), demo[1].Data, "b"), new(Guid.NewGuid(), demo[0].Data, "c") };
        _pokeldn.Script = [Event("starting"), Event("joined"), Done(1, demo[1].Data), Done(2, demo[0].Data), Result(2)];
        var request = new TradeRequest(party, [1, 0], TradeGame.FireRed, new TradeRock.Core.Trading.TrainerProfile("ROCHA", 12345, 54321));
        var result = await engine.RunAttemptAsync(request, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None);

        Assert.Equal(AttemptOutcome.Completed, result.Outcome);
        Assert.Equal([0, 1], events.Where(e => e.Kind == TradeEventKind.TradeDone).Select(e => e.TradeIndex!.Value));
        var sent = _pokeldn.Last!;
        Assert.Equal("trade", _pokeldn.LastMode);
        Assert.Equal("frlg", sent["game"]!.GetValue<string>());
        Assert.Equal("esp32:/dev/ttyUSB0", sent["radio"]!.GetValue<string>());
        Assert.EndsWith("prod.keys", sent["keys"]!.GetValue<string>());
        Assert.Equal(2, sent["trades"]!.GetValue<int>());
        Assert.Equal("firered", sent["version"]!.GetValue<string>());
        Assert.Equal((12345u, 54321u), (sent["trainer"]!["tid"]!.GetValue<uint>(), sent["trainer"]!["sid"]!.GetValue<uint>()));
        // Oferecidos primeiro (na ordem das trocas), depois o preenchimento; .pk3 de 100 bytes decriptado.
        var offers = sent["offers"]!.AsArray().Select(o => Convert.FromBase64String(o!.GetValue<string>())).ToList();
        Assert.Equal(3, offers.Count);
        Assert.Equal(TradeRock.Core.Pokemon.Pk3Reader.Read(demo[1].Data).Sha256, TradeRock.Core.Pokemon.Pk3Reader.Read(offers[0]).Sha256);
        Assert.Equal(TradeRock.Core.Pokemon.Pk3Reader.Read(demo[0].Data).Sha256, TradeRock.Core.Pokemon.Pk3Reader.Read(offers[1]).Sha256);
    }

    [Fact]
    public async Task Motor_esp32_scarlet_com_codigo_e_troca_de_volta()
    {
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", serialPort = "COM3" }));
        var keys = "aes_kek_generation_source = 00112233445566778899aabbccddeeff\naes_key_generation_source = 00112233445566778899aabbccddeeff\nmaster_key_00 = 00112233445566778899aabbccddeeff\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(keys)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
        var engine = _app.Services.GetServices<ITradeEngine>().Single(e => e.Id == "esp32");

        var mine = SwitchPokemon.Stored(SwitchPokemon.Make(TradeRock.Core.Games.GameFamily.Sv));
        var theirs = SwitchPokemon.Make(TradeRock.Core.Games.GameFamily.Sv, species: 93, ot: "RIVAL");
        var request = new TradeRequest([new PartyMember(Guid.NewGuid(), mine, "Pikachu")], [0], TradeGame.Scarlet,
            new TradeRock.Core.Trading.TrainerProfile("Ana", 0xE240, 1), LinkCode: "12345678", TradeBack: true);
        // Troca de volta: na 1ª o Switch manda o Haunter; na 2ª ele volta (evolui lá) e o Pikachu volta para o app.
        _pokeldn.Script = [Event("joined"), Done(1, SwitchPokemon.EncryptedParty(theirs)), Done(2, mine), Result(2)];
        var events = new List<TradeEvent>();
        var result = await engine.RunAttemptAsync(request, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None);

        Assert.Equal(AttemptOutcome.Completed, result.Outcome);
        var done = Assert.Single(events, e => e.Kind == TradeEventKind.TradeDone);
        Assert.Equal(mine, done.ReceivedData);
        var sent = _pokeldn.Last!;
        Assert.Equal("sv", sent["game"]!.GetValue<string>());
        Assert.Equal("12345678", sent["code"]!.GetValue<string>());
        Assert.True(sent["tradeBack"]!.GetValue<bool>());
        Assert.Equal(123456u, sent["trainer"]!["tid"]!.GetValue<uint>()); // como o jogo mostra
        // Oferta do SV: forma de equipe criptografada (344 bytes), como o ajudante PKHeX do pokeldn grava.
        Assert.Equal(344, Convert.FromBase64String(sent["offers"]![0]!.GetValue<string>()).Length);

        _pokeldn.Script = [Event("starting"), Result(0)];
        var nobody = await engine.RunAttemptAsync(request with { TradeBack = false }, _ => ValueTask.CompletedTask, CancellationToken.None);
        Assert.Equal("console_never_joined", nobody.Failure!.Code);
    }

    [Fact]
    public async Task Modo_lan_do_sword_sem_configuracao_nem_codigo()
    {
        // D24: sem prod.keys, rádio nem teste com o Switch; o código não vai para o pokeldn.
        var engine = _app.Services.GetServices<ITradeEngine>().Single(e => e.Id == "lan");
        Assert.True((await engine.CheckAvailabilityAsync(CancellationToken.None)).Available);
        Assert.False(engine.NeedsSetup);
        Assert.True(engine.IgnoresLinkCode);
        Assert.True(engine.Supports(TradeRock.Core.Games.GameFamily.Swsh));
        Assert.False(engine.Supports(TradeRock.Core.Games.GameFamily.Sv));

        var mine = SwitchPokemon.Stored(SwitchPokemon.Make(TradeRock.Core.Games.GameFamily.Swsh));
        var theirs = SwitchPokemon.Stored(SwitchPokemon.Make(TradeRock.Core.Games.GameFamily.Swsh, species: 133, ot: "RIVAL"));
        _pokeldn.Script = [Event("joined"), Done(1, theirs), Result(1)];

        // Pela API: escolhe o modo LAN e troca sem ter configurado a conexão.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync("/api/engines/selected", new { id = "lan" })).StatusCode);
        var engines = await Json(await _client.GetAsync("/api/engines"));
        var lan = engines.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "lan");
        Assert.Equal(["swsh"], lan.GetProperty("games").EnumerateArray().Select(g => g.GetString()));
        using var upload = new MultipartFormDataContent { { new ByteArrayContent(mine), "files", "pikachu.pk8" } };
        var uploaded = await Json(await _client.PostAsync("/api/pokemon", upload));
        var id = uploaded[0].GetProperty("pokemon").GetProperty("id").GetGuid();
        var start = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { id }, offeredSlots = new[] { 0 }, game = "sword", acceptIllegal = true,
            trainer = new { name = "Ana", tid = 123456, sid = 1234 }, linkCode = "12345678",
        });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        for (var i = 0; i < 100 && (await Json(await _client.GetAsync("/api/trades"))).GetArrayLength() == 0; i++)
            await Task.Delay(50);
        var history = await Json(await _client.GetAsync("/api/trades"));
        Assert.Equal("completed", history[0].GetProperty("status").GetString());
        Assert.Equal("lan", _pokeldn.Last!["radio"]!.GetValue<string>());
        Assert.Null(_pokeldn.Last["code"]);

        // Outro jogo no modo LAN: recusado.
        var sv = await _client.PostAsJsonAsync("/api/trades", new
        {
            party = new[] { id }, offeredSlots = new[] { 0 }, game = "scarlet", trainer = new { name = "Ana", tid = 1, sid = 2 },
        });
        Assert.Equal(HttpStatusCode.BadRequest, sv.StatusCode);
    }

    private static JsonObject Event(string kind) => new() { ["type"] = "event", ["kind"] = kind };
    private static JsonObject Done(int index, byte[] data) => new() { ["type"] = "trade_done", ["index"] = index, ["data"] = Convert.ToBase64String(data) };
    private static JsonObject Result(int trades) => new() { ["type"] = "result", ["exitCode"] = 0, ["trades"] = trades };
}

/// <summary>pokeldn falso: grava o último pedido e devolve as mensagens de <see cref="Script"/>.</summary>
internal sealed class FakePokeldn : IPokeldnProcess
{
    public List<JsonObject> Script { get; set; } = [];
    public JsonObject? Last { get; private set; }
    public string? LastMode { get; private set; }

    public bool Available => true;

    public async IAsyncEnumerable<JsonObject> RunAsync(string mode, JsonObject request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        (LastMode, Last) = (mode, request);
        foreach (var message in Script)
        {
            await Task.Yield();
            yield return (JsonObject)message.DeepClone();
        }
    }
}
