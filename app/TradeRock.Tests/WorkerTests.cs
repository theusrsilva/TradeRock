using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Engines;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Setup;
using TradeRock.Core.Trading;

namespace TradeRock.Tests;

public class WorkerLocatorTests
{
    private static string FakeRoot(bool release, bool dev, bool policies = true, bool pkexec = true)
    {
        var root = Directory.CreateTempSubdirectory("traderock-worker-root-").FullName;
        void Touch(string path)
        {
            var full = Path.Combine(root, path.TrimStart('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "");
        }
        foreach (var c in WorkerLocator.Candidates.Where(c => c.Flavor == "release" ? release : c.Flavor == "dev" && dev))
        {
            Touch(c.Launcher);
            if (policies)
                Touch(c.Policy);
        }
        if (pkexec)
            Touch("/usr/bin/pkexec");
        return root;
    }

    [Fact]
    public void Encontra_release_antes_de_dev_salvo_em_desenvolvimento()
    {
        var root = FakeRoot(release: true, dev: true);
        Assert.Equal("release", WorkerLocator.Find(root, writable: _ => false)!.Flavor);
        Assert.Equal("dev", WorkerLocator.Find(root, preferDev: true, writable: _ => false)!.Flavor);
    }

    [Fact]
    public void Launcher_alteravel_pelo_usuario_nao_e_seguro()
    {
        var root = FakeRoot(release: false, dev: true, policies: false, pkexec: false);
        var install = WorkerLocator.Find(root, writable: p => p.EndsWith("traderock-dev", StringComparison.Ordinal))!;
        Assert.False(install.Safe);
        Assert.False(install.PolicyInstalled);
        Assert.False(install.PkexecFound);
        Assert.True(WorkerLocator.Find(root, writable: _ => false)!.Safe);
    }

    [Fact]
    public void Sem_instalacao()
    {
        Assert.Null(WorkerLocator.Find(FakeRoot(false, false), writable: _ => false));
    }
}

public class WorkerHostTests
{
    private static WorkerHost Host(FakeWorker fake) => new(fake, new WorkerOptions(), NullLogger<WorkerHost>.Instance);

    [Fact]
    public async Task Inicia_uma_vez_e_reaproveita()
    {
        var fake = new FakeWorker();
        await using var host = Host(fake);
        await host.GetAsync(CancellationToken.None);
        await host.GetAsync(CancellationToken.None);
        Assert.Equal(1, fake.Launches);
        Assert.True(host.IsRunning);
        Assert.Equal("13809c2", host.Hello!["engineCommit"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(126, "auth_cancelled")]
    [InlineData(127, "not_authorized")]
    public async Task Senha_recusada_vira_codigo(int exitCode, string code)
    {
        var fake = new FakeWorker { PkexecExitCode = exitCode };
        await using var host = Host(fake);
        var e = await Assert.ThrowsAsync<WorkerException>(() => host.GetAsync(CancellationToken.None));
        Assert.Equal(code, e.Code);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public async Task Nao_instalado_ou_inseguro()
    {
        await using var missing = Host(new FakeWorker { Installed = null });
        Assert.Equal("worker_missing", (await Assert.ThrowsAsync<WorkerException>(() => missing.GetAsync(CancellationToken.None))).Code);

        var unsafeFake = new FakeWorker { Installed = new("dev", "/x/traderock-worker", true, Safe: false, true) };
        await using var unsafeHost = Host(unsafeFake);
        Assert.Equal("worker_unsafe", (await Assert.ThrowsAsync<WorkerException>(() => unsafeHost.GetAsync(CancellationToken.None))).Code);
        Assert.Equal(0, unsafeFake.Launches);
    }
}

public class PythonLdnEngineMappingTests
{
    [Fact]
    public void Eventos_e_resultados()
    {
        var done = PythonLdnEngine.ToEvent(new JsonObject
        {
            ["kind"] = "trade_done", ["tradeIndex"] = 1, ["data"] = Convert.ToBase64String(new byte[100]),
        })!;
        Assert.Equal(TradeEventKind.TradeDone, done.Kind);
        Assert.Equal(1, done.TradeIndex);
        Assert.Equal(100, done.ReceivedData!.Length);
        Assert.Null(PythonLdnEngine.ToEvent(new JsonObject { ["kind"] = "desconhecido" }));

        Assert.Equal(AttemptOutcome.Completed, PythonLdnEngine.ToResult(new JsonObject { ["outcome"] = "completed" }, false).Outcome);
        Assert.Equal(AttemptOutcome.Cancelled, PythonLdnEngine.ToResult(new JsonObject { ["outcome"] = "failed" }, cancelRequested: true).Outcome);
        var failed = PythonLdnEngine.ToResult(new JsonObject { ["outcome"] = "failed", ["code"] = "no_network_found", ["retryable"] = true }, false);
        Assert.Equal("no_network_found", failed.Failure!.Code);
        Assert.True(failed.Failure.Retryable);
    }
}

/// <summary>Sistema fixo: Linux nativo com a placa ALFA (phy1). Sem simulação: o motor real fica liberado.</summary>
file sealed class FixedProbe : ISystemProbe
{
    public SystemSnapshot Read() => SetupScenarios.All["linux-compatible"] with { SimulatedScenario = null, Worker = null };
}

/// <summary>Da configuração do assistente até a troca com o motor da placa Wi-Fi, com o worker falso.</summary>
public sealed class PythonLdnApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly FakeWorker _worker = new();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-ldn-").FullName;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private const string Hex = "00112233445566778899aabbccddeeff";

    public async ValueTask InitializeAsync()
    {
        _app = TradeRockServer.Create(["--data-dir", _dataDir], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<IWorkerLauncher>(_worker);
            b.Services.AddSingleton<ISystemProbe, FixedProbe>();
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

    private static readonly string KeysFile = $"aes_kek_generation_source = {Hex}\naes_key_generation_source = {Hex}\nmaster_key_00 = {Hex}\n";

    private async Task UploadKeysAsync()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(KeysFile)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
    }

    private async Task ConfigureWifiAsync()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(KeysFile)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "wifi", phy = "phy1" }));
    }

    [LinuxFact]
    public async Task Assistente_teste_da_placa_e_rede()
    {
        await ConfigureWifiAsync();
        var status = await Json(await _client.GetAsync("/api/setup"));
        Assert.Equal("worker_installed", Check(status, "worker"));
        Assert.Equal("card_test_not_run", Check(status, "card_test"));
        Assert.Equal("network_manager_unconfigured", Check(status, "network_manager"));
        Assert.False(status.GetProperty("readyToTrade").GetBoolean());

        var test = await Json(await _client.PostAsJsonAsync("/api/setup/card-test", new { phy = "phy1" }));
        Assert.True(test.GetProperty("result").GetProperty("passed").GetBoolean());
        Assert.Equal("card_test_passed", Check(test.GetProperty("status"), "card_test"));
        // Falta o teste com o Switch (Entrega 7): ele é essencial com a placa Wi-Fi.
        Assert.Equal("switch_test_not_run", Check(test.GetProperty("status"), "switch_test"));
        Assert.False(test.GetProperty("status").GetProperty("readyToTrade").GetBoolean());

        var sw = await Json(await _client.PostAsJsonAsync("/api/setup/switch-test", new { phy = "phy1" }));
        Assert.Equal("found_decrypted", sw.GetProperty("result").GetProperty("code").GetString());
        Assert.Equal("switch_test_ok", Check(sw.GetProperty("status"), "switch_test"));
        Assert.True(sw.GetProperty("status").GetProperty("readyToTrade").GetBoolean());
        Assert.Equal(KeysFile.Length, _worker.BeaconKeysBytes); // as chaves vão como bytes para o worker

        await Json(await _client.PostAsync("/api/setup/network-config", null));
        Assert.Contains(_worker.Requests, r => r["op"]!.GetValue<string>() == "net_install");
        Assert.Equal(1, _worker.Launches); // a senha é pedida uma vez por sessão

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/setup/card-test", new { phy = "phy7" })).StatusCode);

        var report = await _client.GetStringAsync("/api/setup/report");
        Assert.Contains("Teste da placa (phy1): passed", report);
        Assert.Contains("Teste com o Switch (phy1): found_decrypted", report);
        Assert.Contains("canais indisponíveis: 36", report);
        Assert.DoesNotContain(Hex, report);
    }

    [LinuxFact]
    public async Task Placa_reprovada_bloqueia_a_troca()
    {
        _worker.Mode = "card_fail";
        await ConfigureWifiAsync();
        var test = await Json(await _client.PostAsJsonAsync("/api/setup/card-test", new { phy = "phy1" }));
        Assert.Equal("card_test_failed", Check(test.GetProperty("status"), "card_test"));
        Assert.False(test.GetProperty("status").GetProperty("readyToTrade").GetBoolean());
        var check = test.GetProperty("status").GetProperty("checks").EnumerateArray().First(c => c.GetProperty("id").GetString() == "card_test");
        Assert.Equal("monitor_failed", check.GetProperty("args").GetProperty("code").GetString());

        // Motor selecionável (a placa existe e o worker está instalado), mas a troca continua bloqueada.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync("/api/engines/selected", new { id = "python-ldn" })).StatusCode);
        var response = await _client.PostAsJsonAsync("/api/trades", await TradeBodyAsync(1));
        Assert.Contains("setup_incomplete", await response.Content.ReadAsStringAsync());
    }

    [LinuxFact]
    public async Task Worker_ausente_responde_409()
    {
        _worker.Installed = null;
        await ConfigureWifiAsync();
        var response = await _client.PostAsJsonAsync("/api/setup/card-test", new { phy = "phy1" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("worker_missing", await response.Content.ReadAsStringAsync());
        var engines = await Json(await _client.GetAsync("/api/engines"));
        var ldn = engines.EnumerateArray().First(e => e.GetProperty("id").GetString() == "python-ldn");
        Assert.False(ldn.GetProperty("available").GetBoolean());
        Assert.Equal("worker_missing", ldn.GetProperty("reasonCode").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync("/api/engines/selected", new { id = "python-ldn" })).StatusCode);
    }

    private async Task<object> TradeBodyAsync(int trades = 2)
    {
        var list = await Json(await _client.GetAsync("/api/pokemon"));
        var party = list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToArray();
        return new
        {
            party,
            offeredSlots = Enumerable.Range(0, trades).ToArray(),
            game = "fire_red",
            trainer = new { name = "ROCHA", tid = 12345, sid = 54321 },
        };
    }

    private async Task ReadyAsync()
    {
        await ConfigureWifiAsync();
        await Json(await _client.PostAsJsonAsync("/api/setup/card-test", new { phy = "phy1" }));
        await Json(await _client.PostAsJsonAsync("/api/setup/switch-test", new { phy = "phy1" }));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync("/api/engines/selected", new { id = "python-ldn" })).StatusCode);
    }

    private static JsonElement CheckElement(JsonElement status, string id) =>
        status.GetProperty("checks").EnumerateArray().First(c => c.GetProperty("id").GetString() == id);

    [LinuxTheory]
    [InlineData("found_not_decrypted")]
    [InlineData("other_session")]
    [InlineData("not_found")]
    public async Task Teste_com_o_switch_reprovado_bloqueia_a_troca(string code)
    {
        _worker.BeaconMode = code;
        await ConfigureWifiAsync();
        await Json(await _client.PostAsJsonAsync("/api/setup/card-test", new { phy = "phy1" }));
        var sw = await Json(await _client.PostAsJsonAsync("/api/setup/switch-test", new { phy = "phy1" }));
        var check = CheckElement(sw.GetProperty("status"), "switch_test");
        Assert.Equal("switch_test_failed", check.GetProperty("code").GetString());
        Assert.Equal(code, check.GetProperty("args").GetProperty("code").GetString());
        // prod.keys só com a master_key_00: o anúncio pode ser do protocolo LDN 3.
        Assert.Equal(code == "found_not_decrypted", check.GetProperty("args").TryGetProperty("hint", out _));
        Assert.False(sw.GetProperty("status").GetProperty("readyToTrade").GetBoolean());
    }

    [LinuxFact]
    public async Task Teste_com_o_switch_vale_so_para_o_mesmo_prod_keys()
    {
        await ReadyAsync();
        Assert.True((await Json(await _client.GetAsync("/api/setup"))).GetProperty("readyToTrade").GetBoolean());
        await Task.Delay(20); // a data de gravação do arquivo muda
        await UploadKeysAsync();
        var status = await Json(await _client.GetAsync("/api/setup"));
        Assert.Equal("switch_test_not_run", Check(status, "switch_test"));
        Assert.False(status.GetProperty("readyToTrade").GetBoolean());
    }

    [LinuxFact]
    public async Task Teste_com_o_switch_sem_chaves_e_placa_desconhecida()
    {
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "wifi", phy = "phy1" }));
        Assert.Equal("switch_test_needs_keys", Check(await Json(await _client.GetAsync("/api/setup")), "switch_test"));
        var response = await _client.PostAsJsonAsync("/api/setup/switch-test", new { phy = "phy1" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("keys_missing", await response.Content.ReadAsStringAsync());
        await UploadKeysAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/setup/switch-test", new { phy = "phy7" })).StatusCode);
        Assert.Equal(0, _worker.Launches);
    }

    [LinuxFact]
    public async Task Cancelar_o_teste_com_o_switch_avisa_o_worker()
    {
        _worker.BeaconMode = "hang";
        await ConfigureWifiAsync();
        using var cts = new CancellationTokenSource();
        var request = _client.PostAsJsonAsync("/api/setup/switch-test", new { phy = "phy1" }, cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!_worker.Requests.Any(r => r["op"]!.GetValue<string>() == "beacon_test") && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        while (!_worker.Requests.Any(r => r["op"]!.GetValue<string>() == "cancel") && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Contains(_worker.Requests, r => r["op"]!.GetValue<string>() == "cancel");
        Assert.Equal("switch_test_not_run", Check(await Json(await _client.GetAsync("/api/setup")), "switch_test"));
    }

    private async Task<List<JsonElement>> EventsUntilFinishedAsync(Func<JsonElement, Task>? onEvent = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/trades/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var events = new List<JsonElement>();
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            var e = JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone();
            events.Add(e);
            if (onEvent is not null)
                await onEvent(e);
            if (e.GetProperty("kind").GetString() == "session_finished")
                break;
        }
        return events;
    }

    [LinuxFact]
    public async Task Troca_real_de_ponta_a_ponta_com_o_worker()
    {
        await ReadyAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsJsonAsync("/api/trades", await TradeBodyAsync())).StatusCode);
        var events = await EventsUntilFinishedAsync();

        Assert.Equal("completed", events[^1].GetProperty("status").GetString());
        var received = events.Where(e => e.GetProperty("kind").GetString() == "trade_done").ToList();
        Assert.Equal(2, received.Count);
        Assert.All(received, e => Assert.NotEqual(JsonValueKind.Null, e.GetProperty("receivedPokemonId").ValueKind));
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "beacon_decrypted");

        // O worker recebeu tudo pronto: phy, chaves (como bytes), time e treinador.
        var start = _worker.Requests.Single(r => r["op"]!.GetValue<string>() == "start_trade");
        Assert.Equal("phy1", start["phy"]!.GetValue<string>());
        Assert.Contains(Hex, Encoding.UTF8.GetString(Convert.FromBase64String(start["keys"]!.GetValue<string>())));
        Assert.Equal("firered", start["game"]!.GetValue<string>());
        Assert.Equal(54321, start["sid"]!.GetValue<int>());
        Assert.Equal(100, start["approvalSeconds"]!.GetValue<int>());

        // As chaves nunca aparecem no histórico.
        var history = await _client.GetStringAsync("/api/trades");
        Assert.DoesNotContain(Hex, history);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Hex))[..16], history);
    }

    [LinuxFact]
    public async Task Jogos_novos_vao_pelo_pokeldn_no_worker()
    {
        await ConfigureWifiAsync();
        var engine = _app.Services.GetServices<ITradeEngine>().Single(e => e.Id == PythonLdnEngine.EngineId);
        Assert.True(engine.Supports(TradeRock.Core.Games.GameFamily.Za));
        Assert.True(engine.UsesPokeldn(TradeRock.Core.Games.GameFamily.Za));
        Assert.False(engine.UsesPokeldn(TradeRock.Core.Games.GameFamily.Frlg));

        var received = SwitchPokemon.Stored(SwitchPokemon.Make(TradeRock.Core.Games.GameFamily.Za, species: 133));
        _worker.PokeldnReceived = Convert.ToBase64String(received);
        var offer = SwitchPokemon.Stored(SwitchPokemon.Make(TradeRock.Core.Games.GameFamily.Za));
        var request = new TradeRequest([new PartyMember(Guid.NewGuid(), offer, "Pikachu")], [0], TradeGame.LegendsZA,
            new TradeRock.Core.Trading.TrainerProfile("Ana", 1, 2), LinkCode: "12345678");
        var events = new List<TradeEvent>();
        var result = await engine.RunAttemptAsync(request, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None);

        Assert.Equal(AttemptOutcome.Completed, result.Outcome);
        Assert.Equal(received, Assert.Single(events, e => e.Kind == TradeEventKind.TradeDone).ReceivedData);
        var sent = _worker.Requests.Single(r => r["op"]?.GetValue<string>() == "start_pokeldn");
        Assert.Equal("phy1", sent["phy"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(sent["keys"]?.GetValue<string>()));
        var inner = sent["request"]!.AsObject();
        Assert.Equal("za", inner["game"]!.GetValue<string>());
        Assert.Null(inner["radio"]);  // placa Wi-Fi: o worker escolhe
        Assert.Null(inner["keys"]);   // as chaves vão à parte, só para o worker
    }

    [LinuxFact]
    public async Task Cancelar_pede_ao_worker_e_espera_ele_sair()
    {
        await ReadyAsync();
        _worker.Mode = "hang";
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsJsonAsync("/api/trades", await TradeBodyAsync(1))).StatusCode);
        var cancelled = false;
        var events = await EventsUntilFinishedAsync(async e =>
        {
            if (!cancelled && e.GetProperty("kind").GetString() == "join_requested")
            {
                cancelled = true;
                await _client.PostAsync("/api/trades/current/cancel", null);
            }
        });
        Assert.Equal("cancelled", events[^1].GetProperty("status").GetString());
        Assert.Contains(_worker.Requests, r => r["op"]!.GetValue<string>() == "cancel");
    }

    [LinuxFact]
    public async Task Sala_nao_encontrada_tenta_de_novo()
    {
        await ReadyAsync();
        _worker.Mode = "no_network";
        await Json(await _client.PutAsJsonAsync("/api/settings/trade", new { maxAttempts = 2, retryDelaySeconds = 0 }));
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsJsonAsync("/api/trades", await TradeBodyAsync(1))).StatusCode);
        var events = await EventsUntilFinishedAsync();
        Assert.Equal("failed", events[^1].GetProperty("status").GetString());
        Assert.Equal(2, events.Count(e => e.GetProperty("kind").GetString() == "attempt_started"));
        Assert.Contains(events, e => e.GetProperty("errorCode").GetString() == "no_network_found");
    }

    [LinuxFact]
    public async Task Simulacao_do_assistente_nao_libera_o_motor_real()
    {
        await ReadyAsync();
        var settings = _app.Services.GetRequiredService<TradeRock.Core.Storage.SettingsStore>();
        await settings.SetAsync(SetupService.SimulationKey, "linux-compatible", CancellationToken.None);
        var response = await _client.PostAsJsonAsync("/api/trades", await TradeBodyAsync(1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("setup_simulated", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Sistema sem iw e sem sysctl: o worker traz o iw embutido, então só o sysctl falta.</summary>
file sealed class NoToolsProbe : ISystemProbe
{
    public SystemSnapshot Read() => SetupScenarios.All["linux-compatible"] with { SimulatedScenario = null, Worker = null, MissingTools = ["iw", "sysctl"] };
}

public sealed class BundledToolsTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-tools-").FullName;
    private WebApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        var worker = new FakeWorker { Installed = new("dev", "/usr/local/lib/traderock-dev/traderock-worker", true, true, true, BundledTools: true) };
        _app = TradeRockServer.Create(["--data-dir", _dataDir], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<IWorkerLauncher>(worker);
            b.Services.AddSingleton<ISystemProbe, NoToolsProbe>();
        });
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        try { Directory.Delete(_dataDir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Ferramentas_embutidas_nao_sao_cobradas()
    {
        var setup = _app.Services.GetRequiredService<SetupService>();
        await setup.UpdateStateAsync(new SetupStatePatch(Radio: "wifi", Phy: "phy1"), CancellationToken.None);
        var status = await setup.StatusAsync(CancellationToken.None);
        var tools = status.Checks.Single(c => c.Id == "tools");
        Assert.Equal("sysctl", tools.Args!["tools"]);
        Assert.Equal("sudo apt install procps", tools.Fix);
    }
}
