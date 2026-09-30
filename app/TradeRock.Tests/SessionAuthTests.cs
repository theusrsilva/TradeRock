using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using TradeRock.App;
using TradeRock.Core;

namespace TradeRock.Tests;

public sealed class SessionAuthTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private WebApplication _app = null!;
    private string _dataDir = null!;

    public async ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("traderock-test-").FullName;
        _app = TradeRockServer.Create(["--data-dir", _dataDir], _token, b => b.WebHost.UseTestServer());
        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        // Windows: o pool do SQLite segura o arquivo do banco aberto até ser limpo.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private HttpClient Client(string host = "127.0.0.1")
    {
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri($"http://{host}/");
        return client;
    }

    [Fact]
    public async Task Api_sem_token_retorna_401()
    {
        var response = await Client().GetAsync("/api/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pagina_sem_token_retorna_401()
    {
        var response = await Client().GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_errado_na_url_retorna_401_sem_cookie()
    {
        var response = await Client().GetAsync("/?t=" + SessionToken.Generate().Value, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Token_na_url_vira_cookie_httponly_e_redireciona_sem_token()
    {
        var response = await Client().GetAsync($"/?t={_token.Value}&x=1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?x=1", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(_token.Value, response.Headers.Location?.OriginalString ?? "");

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{SessionAuthMiddleware.CookieName}=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cookie_valido_libera_api()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionAuthMiddleware.CookieName}={_token.Value}");

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"app\":\"TradeRock\"", body);
    }

    [Fact]
    public async Task Cabecalho_valido_libera_api()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Host_desconhecido_e_recusado_mesmo_com_token()
    {
        // Proteção contra DNS rebinding: um site externo apontando para 127.0.0.1 chega com outro Host.
        var client = Client("evil.example");
        client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_inexistente_retorna_404_e_nao_o_index()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);

        var response = await client.GetAsync("/api/nao-existe", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
