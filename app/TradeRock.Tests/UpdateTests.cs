using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Updates;

namespace TradeRock.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("01.00.01", "01.00.00", true)]
    [InlineData("1.1", "01.00.09", true)]
    [InlineData("01.00.00", "0.00.04", true)]
    [InlineData("v02.00.00", "01.09.09", true)]
    [InlineData("01.00.00", "1.0.0", false)]
    [InlineData("0.00.04", "01.00.00", false)]
    [InlineData("01.00.01-rc.1", "01.00.00", false)] // pré-lançamento fica de fora
    [InlineData("lixo", "01.00.00", false)]
    public void Versao_nova(string candidate, string current, bool newer) =>
        Assert.Equal(newer, AppVersions.IsNewer(candidate, current));

    [Fact]
    public void Arquivo_de_cada_instalacao_e_hash()
    {
        Assert.Equal("TradeRock-Setup-01.00.01.exe", AppVersions.AssetName(InstallKind.WindowsInstaller, "01.00.01"));
        Assert.Equal("TradeRock-01.00.01-x86_64.AppImage", AppVersions.AssetName(InstallKind.AppImage, "01.00.01"));
        Assert.Equal("traderock_01.00.01_amd64.deb", AppVersions.AssetName(InstallKind.Deb, "01.00.01"));
        Assert.Null(AppVersions.AssetName(InstallKind.None, "01.00.01"));

        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  outro.exe\n{hash}  TradeRock-Setup-01.00.01.exe\n";
        Assert.Equal(hash, AppVersions.HashOf(sums, "TradeRock-Setup-01.00.01.exe"));
        Assert.Null(AppVersions.HashOf(sums, "TradeRock-Setup-9.exe"));
    }

    [Fact]
    public void Release_so_com_arquivos_do_proprio_repositorio()
    {
        var json = UpdateFixture.ReleaseJson("v01.00.01", ("SHA256SUMS", null), ("TradeRock-Setup-01.00.01.exe", null),
            ("TradeRock-01.00.01-x86_64.AppImage", "https://exemplo.com/TradeRock-01.00.01-x86_64.AppImage"));
        var release = UpdateService.Parse(json, UpdateFixture.Repo)!;
        Assert.Equal("01.00.01", release.Version);
        Assert.Equal(["SHA256SUMS", "TradeRock-Setup-01.00.01.exe"], release.Assets.Select(a => a.Name));

        Assert.Null(UpdateService.Parse(UpdateFixture.ReleaseJson("v01.00.01-rc.1"), UpdateFixture.Repo));
        Assert.Null(UpdateService.Parse(UpdateFixture.ReleaseJson("v01.00.01", prerelease: true), UpdateFixture.Repo));
    }
}

internal static class UpdateFixture
{
    public const string Repo = "theusrsilva/TradeRock";

    public static string ReleaseJson(string tag, params (string Name, string? Url)[] assets) => ReleaseJson(tag, false, assets);

    public static string ReleaseJson(string tag, bool prerelease, params (string Name, string? Url)[] assets) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            prerelease,
            draft = false,
            html_url = $"https://github.com/{Repo}/releases/tag/{tag}",
            body = "Novidades",
            published_at = "2026-11-01T00:00:00Z",
            assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url ?? Download(tag, a.Name), size = 3 }),
        });

    public static string Download(string tag, string name) => $"https://github.com/{Repo}/releases/download/{tag}/{name}";
}

/// <summary>O GitHub de mentira: só responde às URLs conhecidas e registra os pedidos.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public Dictionary<string, (HttpStatusCode Status, byte[] Body)> Routes { get; } = [];
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        lock (Requests)
            Requests.Add(url);
        Assert.Contains("TradeRock", request.Headers.UserAgent.ToString());
        var (status, body) = Routes.TryGetValue(url, out var r) ? r : (HttpStatusCode.NotFound, []);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}

internal sealed class FakeInstaller : IUpdateInstaller
{
    public InstallKind Kind { get; set; } = InstallKind.WindowsInstaller;
    public byte[]? Installed { get; private set; }
    public string? Sha { get; private set; }

    public Task InstallAsync(string file, string sha256, CancellationToken ct)
    {
        Installed = File.ReadAllBytes(file);
        Sha = sha256;
        return Task.CompletedTask;
    }
}

internal sealed class FakeExit : IAppExit
{
    public int Calls { get; private set; }

    public Task ExitAsync()
    {
        Calls++;
        return Task.CompletedTask;
    }
}

public sealed class UpdateApiTests : IAsyncLifetime
{
    private const string Latest = $"https://api.github.com/repos/{UpdateFixture.Repo}/releases/latest";
    private const string Tag = "v99.00.00";
    private const string Exe = "TradeRock-Setup-99.00.00.exe";
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("instalador novo");

    private readonly SessionToken _token = SessionToken.Generate();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-update-").FullName;
    private readonly FakeGitHub _github = new();
    private readonly FakeInstaller _installer = new();
    private readonly FakeExit _exit = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _app = TradeRockServer.Create(["--data-dir", _dataDir], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddHttpClient(nameof(UpdateService)).ConfigurePrimaryHttpMessageHandler(() => _github);
            b.Services.AddSingleton<IUpdateInstaller>(_installer);
            b.Services.AddSingleton<IAppExit>(_exit);
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

    private void Publish(string sha)
    {
        _github.Routes[Latest] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(UpdateFixture.ReleaseJson(Tag, ("SHA256SUMS", null), (Exe, null))));
        _github.Routes[UpdateFixture.Download(Tag, "SHA256SUMS")] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes($"{sha}  {Exe}\n"));
        _github.Routes[UpdateFixture.Download(Tag, Exe)] = (HttpStatusCode.OK, Installer);
    }

    private async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<JsonElement> WaitAsync(params string[] stages)
    {
        for (var i = 0; i < 200; i++)
        {
            var status = await Json(await _client.GetAsync("/api/update"));
            if (stages.Contains(status.GetProperty("stage").GetString()))
                return status;
            await Task.Delay(20);
        }
        throw new TimeoutException();
    }

    [Fact]
    public async Task Procura_baixa_confere_e_instala()
    {
        var status = await Json(await _client.GetAsync("/api/update"));
        Assert.True(status.GetProperty("autoCheck").GetBoolean());
        Assert.False(status.GetProperty("available").GetBoolean());
        Assert.Empty(_github.Requests); // sem a procura ligada no início dos testes (instalação "none" não procura sozinha)

        Publish(Convert.ToHexStringLower(SHA256.HashData(Installer)));
        status = await Json(await _client.PostAsync("/api/update/check", null));
        Assert.True(status.GetProperty("available").GetBoolean());
        Assert.Equal("99.00.00", status.GetProperty("latest").GetProperty("version").GetString());
        Assert.Equal("windows_installer", status.GetProperty("kind").GetString());

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/update/install", null)).StatusCode);
        status = await WaitAsync("restarting", "error");
        Assert.Equal("restarting", status.GetProperty("stage").GetString());
        Assert.Equal(Installer, _installer.Installed);
        Assert.Equal(1, _exit.Calls);
        // Só o GitHub, e nada além do Release.
        Assert.All(_github.Requests, u => Assert.True(u.StartsWith("https://api.github.com/", StringComparison.Ordinal)
            || u.StartsWith($"https://github.com/{UpdateFixture.Repo}/releases/download/", StringComparison.Ordinal), u));
    }

    [Fact]
    public async Task Arquivo_que_nao_confere_nao_e_instalado()
    {
        Publish(new string('0', 64));
        await Json(await _client.PostAsync("/api/update/check", null));
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/update/install", null)).StatusCode);
        var status = await WaitAsync("error", "restarting");
        Assert.Equal("update_hash_mismatch", status.GetProperty("errorCode").GetString());
        Assert.Null(_installer.Installed);
        Assert.Equal(0, _exit.Calls);
    }

    [Fact]
    public async Task Sem_versao_nova_ou_sem_internet()
    {
        _github.Routes[Latest] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(UpdateFixture.ReleaseJson("v0.0.1")));
        var status = await Json(await _client.PostAsync("/api/update/check", null));
        Assert.False(status.GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync("/api/update/install", null)).StatusCode);

        _github.Routes[Latest] = (HttpStatusCode.ServiceUnavailable, []);
        var failed = await _client.PostAsync("/api/update/check", null);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal("update_check_failed", JsonDocument.Parse(await failed.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());

        status = await Json(await _client.PutAsJsonAsync("/api/update/auto", new { on = false }));
        Assert.False(status.GetProperty("autoCheck").GetBoolean());
    }

    [Fact]
    public void So_na_janela_do_pc() =>
        Assert.True(TradeRock.App.Server.AccessPolicy.IsLocalOnly(new Microsoft.AspNetCore.Http.DefaultHttpContext { Request = { Path = "/api/update/install" } }.Request));
}
