using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Logging;

namespace TradeRock.Tests;

file sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public class LogTests
{
    private const string Key = "00112233445566778899aabbccddeeff";

    [Fact]
    public void Redator_apaga_chaves_token_e_dados()
    {
        var text = LogRedactor.Redact($"master_key_00 = {Key} url /?t=segredo123&x=1 pk {new string('A', 80)}");
        Assert.DoesNotContain(Key, text);
        Assert.DoesNotContain("segredo123", text);
        Assert.DoesNotContain(new string('A', 80), text);
        Assert.Contains("[hex]", text);
        Assert.Contains("?t=[token]&x=1", text);
        // Texto comum e números curtos ficam.
        Assert.Equal("Invalid channel: 2 (COM4)", LogRedactor.Redact("Invalid channel: 2 (COM4)"));
    }

    [Fact]
    public void Arquivo_por_dia_guarda_sete_dias_e_le_as_ultimas_linhas()
    {
        var dir = Directory.CreateTempSubdirectory("traderock-log-").FullName;
        try
        {
            var time = new FixedTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
            var log = new FileLog(dir, time);
            log.Write("INF", "Antigo", "dia 20");
            time.Now = time.Now.AddDays(10);
            log.Write("ERR", "Esp32Setup", $"falhou\ncom a chave {Key}");
            log.Write("INF", "PokeldnProcess", "terminou");

            var all = log.Tail(10, problemsOnly: false);
            Assert.Equal(["dia 20", $"falhou\ncom a chave [hex]", "terminou"], all.Select(l => l.Message));
            Assert.Equal("ERR", all[1].Level);
            Assert.Equal("Esp32Setup", all[1].Category);
            Assert.Single(log.Tail(10, problemsOnly: true));
            Assert.Equal(["terminou"], log.Tail(1, problemsOnly: false).Select(l => l.Message));

            // Ao abrir de novo, os arquivos com mais de sete dias saem.
            var reopened = new FileLog(dir, time);
            Assert.Equal(2, reopened.Tail(10, problemsOnly: false).Count);
            Assert.DoesNotContain(Key, string.Concat(reopened.Files().Select(File.ReadAllText)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Saida_do_processo_vai_para_o_log_so_o_fim()
    {
        var dir = Directory.CreateTempSubdirectory("traderock-log-").FullName;
        try
        {
            var log = new FileLog(dir);
            using var factory = LoggerFactory.Create(b => b.AddProvider(new FileLoggerProvider(log)));
            var logger = factory.CreateLogger("TradeRock.Core.Engines.Pokeldn.PokeldnProcess");
            ProcessLog.Write(logger, "pokeldn board", 1, string.Join('\n', Enumerable.Range(1, ProcessLog.MaxLines + 5).Select(i => $"linha {i}")));
            var line = Assert.Single(log.Tail(10, problemsOnly: true));
            Assert.Equal("PokeldnProcess", line.Category);
            Assert.StartsWith("pokeldn board terminou com o código 1.", line.Message);
            Assert.DoesNotContain("linha 5\n", line.Message);
            Assert.EndsWith($"linha {ProcessLog.MaxLines + 5}", line.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public sealed class LogApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-logapi-").FullName;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _app = TradeRockServer.Create(["--data-dir", _dataDir], _token, b => b.WebHost.UseTestServer());
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

    [Fact]
    public async Task Aba_logs_mostra_os_avisos_e_baixa_o_arquivo()
    {
        var logger = _app.Services.GetRequiredService<ILogger<LogApiTests>>();
        logger.LogWarning("ESP32: esp32_error. Invalid channel: 2");
        logger.LogInformation("tudo certo");

        var json = JsonDocument.Parse(await _client.GetStringAsync("/api/logs?problems=true")).RootElement;
        var lines = json.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("message").GetString()).ToList();
        Assert.Contains("ESP32: esp32_error. Invalid channel: 2", lines);
        Assert.DoesNotContain("tudo certo", lines);
        Assert.Equal(FileLog.KeepDays, json.GetProperty("keepDays").GetInt32());

        var file = await _client.GetAsync("/api/logs/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Contains("tudo certo", await file.Content.ReadAsStringAsync());
        Assert.True(TradeRock.App.Server.AccessPolicy.IsLocalOnly(new Microsoft.AspNetCore.Http.DefaultHttpContext { Request = { Path = "/api/logs" } }.Request));
    }
}
