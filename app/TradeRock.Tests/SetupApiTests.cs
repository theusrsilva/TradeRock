using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using TradeRock.App;
using TradeRock.Core;

namespace TradeRock.Tests;

public sealed class SetupApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly List<WebApplication> _apps = [];
    private readonly List<string> _dirs = [];

    private async Task<(HttpClient Client, string DataDir)> Start(bool development)
    {
        var dir = Directory.CreateTempSubdirectory("traderock-setup-").FullName;
        _dirs.Add(dir);
        string[] args = development
            ? ["--data-dir", dir, "--environment", "Development"]
            : ["--data-dir", dir];
        var app = TradeRockServer.Create(args, _token, b => b.WebHost.UseTestServer());
        await app.StartAsync();
        _apps.Add(app);
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://127.0.0.1/");
        client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
        return (client, dir);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var app in _apps)
            await app.DisposeAsync();
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Status_do_sistema_real_e_catalogo()
    {
        var (client, _) = await Start(development: false);
        var status = await Json(await client.GetAsync("/api/setup"));

        Assert.False(status.GetProperty("readyToTrade").GetBoolean());
        Assert.Equal("", status.GetProperty("system").GetProperty("user").ValueKind == JsonValueKind.Null
            ? "" : status.GetProperty("system").GetProperty("user").GetProperty("name").GetString());
        Assert.Contains(status.GetProperty("checks").EnumerateArray(), c => c.GetProperty("id").GetString() == "keys");
        Assert.Equal("2026-09-29", status.GetProperty("catalogUpdatedAt").GetString());

        var catalog = await Json(await client.GetAsync("/api/setup/catalog"));
        Assert.Contains(catalog.GetProperty("wifi").EnumerateArray(), w => w.GetProperty("status").GetString() == "recommended");
    }

    [Fact]
    public async Task Simulacao_so_existe_em_desenvolvimento()
    {
        var (prod, _) = await Start(development: false);
        Assert.Equal(HttpStatusCode.NotFound, (await prod.GetAsync("/api/setup/scenarios")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await prod.PutAsJsonAsync("/api/setup/simulation", new { scenario = "wsl" })).StatusCode);

        var (dev, _) = await Start(development: true);
        var list = await Json(await dev.GetAsync("/api/setup/scenarios"));
        Assert.Equal(9, list.GetProperty("scenarios").GetArrayLength());

        var simulated = await Json(await dev.PutAsJsonAsync("/api/setup/simulation", new { scenario = "linux-problematic" }));
        Assert.Equal("wifi_problematic", simulated.GetProperty("advice").GetProperty("situation").GetString());
        Assert.Equal("linux-problematic", simulated.GetProperty("system").GetProperty("simulatedScenario").GetString());

        var bad = await dev.PutAsJsonAsync("/api/setup/simulation", new { scenario = "marte" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Escolhas_do_assistente_e_relatorio()
    {
        var (client, _) = await Start(development: true);
        await client.PutAsJsonAsync("/api/setup/simulation", new { scenario = "linux-esp32" });

        var status = await Json(await client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", esp32Board = "esp32-s3", serialPort = "/dev/ttyUSB0" }));
        var checks = status.GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, c => c.GetProperty("id").GetString() == "serial_group" && c.GetProperty("status").GetString() == "error");
        Assert.Equal("esp32", status.GetProperty("state").GetProperty("radio").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/setup/state", new { radio = "bluetooth" })).StatusCode);

        var report = await client.GetStringAsync("/api/setup/report");
        Assert.Contains("cenário simulado: linux-esp32", report);

        var reset = await Json(await client.PostAsync("/api/setup/reset", null));
        Assert.Equal(JsonValueKind.Null, reset.GetProperty("state").GetProperty("radio").ValueKind);
    }

    [Fact]
    public async Task Enviar_prod_keys_valida_guarda_e_nunca_devolve_valores()
    {
        var (client, dir) = await Start(development: false);
        const string hex = "00112233445566778899aabbccddeeff";
        var text = $"aes_kek_generation_source = {hex}\naes_key_generation_source = {hex}\nmaster_key_00 = {hex}\n";

        using (var bad = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("oi")), "file", "prod.keys" } })
        {
            var response = await client.PostAsync("/api/setup/keys", bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(File.Exists(Path.Combine(dir, "keys", "prod.keys")));
        }

        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(text)), "file", "prod.keys" } };
        var ok = await client.PostAsync("/api/setup/keys", form);
        var body = await ok.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.DoesNotContain(hex, body);

        var path = Path.Combine(dir, "keys", "prod.keys");
        Assert.True(File.Exists(path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));

        var status = await client.GetStringAsync("/api/setup");
        Assert.Contains("\"keys_ok\"", status);
        Assert.DoesNotContain(hex, status);
        Assert.DoesNotContain(hex, await client.GetStringAsync("/api/setup/report"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/setup/keys")).StatusCode);
        Assert.False(File.Exists(path));
    }
}
