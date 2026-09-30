using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TradeRock.App;
using TradeRock.App.Server;
using TradeRock.Core;
using TradeRock.Core.Engines;

namespace TradeRock.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_argon2id_confere_e_recusa_senha_errada()
    {
        var hash = PasswordHasher.Hash("senha-forte-1");
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash);
        Assert.True(PasswordHasher.Verify("senha-forte-1", hash));
        Assert.False(PasswordHasher.Verify("senha-forte-2", hash));
        Assert.False(PasswordHasher.NeedsRehash(hash));
    }

    [Fact]
    public void Sal_aleatorio_e_formato_invalido_recusado()
    {
        Assert.NotEqual(PasswordHasher.Hash("x"), PasswordHasher.Hash("x"));
        Assert.False(PasswordHasher.Verify("x", "texto qualquer"));
        Assert.False(PasswordHasher.Verify("x", "$argon2i$v=19$m=19456,t=2,p=1$AAAA$AAAA"));
        Assert.False(PasswordHasher.Verify("x", "$argon2id$v=19$m=99999999,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }
}

/// <summary>
/// Modo servidor pela API. A rede é simulada: um filtro marca as requisições com o cabeçalho X-Test-Lan como
/// vindas do endereço da rede (192.168.50.10:8337) e o "listener" só atualiza os hosts aceitos.
/// </summary>
public sealed class ServerModeTests : IAsyncLifetime
{
    private const string LanHost = "192.168.50.10";
    private const int LanPort = 8337;

    private readonly SessionToken _token = SessionToken.Generate();
    private WebApplication _app = null!;
    private string _dataDir = null!;
    private HttpClient _local = null!;

    public async ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("traderock-server-").FullName;
        _app = TradeRockServer.Create(["--data-dir", _dataDir, "--Engines:Fake:SpeedFactor=0"], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<INetworkInterfaces>(new FakeInterfaces(
                new("eth0", IPAddress.Parse(LanHost), false), new("wlan0", IPAddress.Parse("192.168.50.11"), true)));
            b.Services.AddSingleton<ILanListener, FakeLanListener>();
            b.Services.AddSingleton<IStartupFilter, LanMarker>();
        });
        await _app.StartAsync();
        _local = _app.GetTestClient();
        _local.BaseAddress = new Uri("http://127.0.0.1/");
        _local.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
    }

    public async ValueTask DisposeAsync()
    {
        _local.Dispose();
        await _app.DisposeAsync();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task EnableAsync()
    {
        var response = await _local.PutAsJsonAsync("/api/server", new { enabled = true, @interface = "eth0", port = LanPort }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CreateAccountAsync(string username, string password = "senha-forte-1", string role = "user")
    {
        var response = await _local.PostAsJsonAsync("/api/accounts", new { username, password, role }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private LanClient Lan(string remote = "192.168.50.20") => new(_app.GetTestClient(), remote);

    private async Task<LanClient> LoginAsync(string username, string password = "senha-forte-1")
    {
        var lan = Lan();
        var response = await lan.PostAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return lan;
    }

    [Fact]
    public async Task Ligar_mostra_endereco_e_qr_code_e_desligar_derruba_os_logins()
    {
        var status = await _local.GetFromJsonAsync<JsonElement>("/api/server", Ct);
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(ServerModeSettings.DefaultPort, status.GetProperty("port").GetInt32());

        await EnableAsync();
        status = await _local.GetFromJsonAsync<JsonElement>("/api/server", Ct);
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal($"http://{LanHost}:{LanPort}/", status.GetProperty("url").GetString());

        var qr = await _local.GetAsync("/api/server/qr", Ct);
        Assert.Equal("image/svg+xml", qr.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", await qr.Content.ReadAsStringAsync(Ct));

        await CreateAccountAsync("ana");
        var ana = await LoginAsync("ana");
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/vault")).StatusCode);

        await _local.PutAsJsonAsync("/api/server", new { enabled = false }, Ct);
        // Desligado, o endereço da rede sai dos hosts aceitos (e o login antigo não vale mais).
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.GetAsync("/api/vault")).StatusCode);
        await EnableAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ana.GetAsync("/api/vault")).StatusCode);
    }

    [Fact]
    public async Task Pela_rede_a_pagina_abre_mas_a_api_pede_login_e_o_token_da_janela_nao_vale()
    {
        await EnableAsync();
        var lan = Lan();
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/api/health")).StatusCode);

        var me = await lan.GetJsonAsync("/api/auth/me");
        Assert.False(me.GetProperty("authenticated").GetBoolean());
        Assert.True(me.GetProperty("serverMode").GetBoolean());

        var vault = await lan.GetAsync("/api/vault");
        Assert.Equal(HttpStatusCode.Unauthorized, vault.StatusCode);
        Assert.Contains("login_required", await vault.Content.ReadAsStringAsync(Ct));

        lan.Client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await lan.GetAsync("/api/vault")).StatusCode);
        var withToken = await lan.GetAsync($"/?t={_token.Value}");
        Assert.NotEqual(HttpStatusCode.Redirect, withToken.StatusCode);
        Assert.False(withToken.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Janela_local_nao_faz_login_pela_rede()
    {
        await EnableAsync();
        var response = await _local.PostAsJsonAsync("/api/auth/login", new { username = "x", password = "y" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var me = await _local.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        Assert.True(me.GetProperty("local").GetBoolean());
        Assert.Equal("admin", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Senha_errada_recusa_e_muitas_tentativas_travam_a_conta()
    {
        await EnableAsync();
        await CreateAccountAsync("bia");
        var lan = Lan();
        for (var i = 0; i < LoginThrottle.MaxPerAccount; i++)
        {
            var wrong = await lan.PostAsync("/api/auth/login", new { username = "bia", password = "errada-123" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }
        // Até a senha certa espera, e de outro endereço também.
        var locked = await Lan("192.168.50.30").PostAsync("/api/auth/login", new { username = "bia", password = "senha-forte-1" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Contains("too_many_attempts", await locked.Content.ReadAsStringAsync(Ct));

        // Conta que não existe responde igual à senha errada.
        var unknown = await Lan("192.168.50.31").PostAsync("/api/auth/login", new { username = "ninguem", password = "senha-forte-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Contains("login_failed", await unknown.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Alteracao_pela_rede_exige_o_token_anti_csrf_e_a_mesma_origem()
    {
        await EnableAsync();
        await CreateAccountAsync("caio");
        var lan = await LoginAsync("caio");

        var noCsrf = await lan.PostAsync("/api/vault/boxes", new { name = "Sem token" }, csrf: false);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Contains("csrf", await noCsrf.Content.ReadAsStringAsync(Ct));

        var otherSite = await lan.PostAsync("/api/vault/boxes", new { name = "Outro site" }, origin: "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, otherSite.StatusCode);

        var ok = await lan.PostAsync("/api/vault/boxes", new { name = "Caixa" }, origin: $"http://{LanHost}:{LanPort}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Cada_conta_tem_o_proprio_cofre()
    {
        await EnableAsync();
        await CreateAccountAsync("duda");
        var duda = await LoginAsync("duda");

        // Conta nova começa com os Pokémon de demonstração, no cofre dela.
        var dudaPokemon = await duda.GetJsonAsync("/api/pokemon");
        var localPokemon = await _local.GetFromJsonAsync<JsonElement>("/api/pokemon", Ct);
        Assert.Equal(2, dudaPokemon.GetArrayLength());
        var dudaIds = dudaPokemon.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToHashSet();
        var localIds = localPokemon.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(localIds, dudaIds.Contains);

        // O Pokémon do dono não existe para a Duda.
        Assert.Equal(HttpStatusCode.NotFound, (await duda.GetAsync($"/api/pokemon/{localIds[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await duda.GetAsync($"/api/pokemon/{localIds[0]}/file")).StatusCode);

        // Caixa e tag da Duda não aparecem para o dono (e o mesmo nome de tag pode existir nos dois).
        Assert.Equal(HttpStatusCode.OK, (await duda.PostAsync("/api/vault/boxes", new { name = "Da Duda" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await duda.PostAsync("/api/vault/tags", new { name = "Favoritos", color = "sky" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _local.PostAsJsonAsync("/api/vault/tags", new { name = "Favoritos", color = "red" }, Ct)).StatusCode);
        var overview = await _local.GetFromJsonAsync<JsonElement>("/api/vault", Ct);
        Assert.DoesNotContain(overview.GetProperty("boxes").EnumerateArray(), b => b.GetProperty("name").GetString() == "Da Duda");

        // Tentar mexer no Pokémon do dono pela lixeira não tem efeito.
        await duda.PostAsync("/api/vault/trash", new { ids = new[] { localIds[0] } });
        var after = await _local.GetFromJsonAsync<JsonElement>("/api/pokemon", Ct);
        Assert.Equal(localPokemon.GetArrayLength(), after.GetArrayLength());

        // "Meu treinador" também é por conta.
        await duda.PutAsync("/api/settings/my-trainer", new { name = "DUDA", tid = 1, sid = 2, gender = 1, badges = 8, hasNationalDex = true });
        var mine = await _local.GetAsync("/api/settings/my-trainer", Ct);
        Assert.Equal(HttpStatusCode.NoContent, mine.StatusCode);
    }

    [Fact]
    public async Task Apagar_conta_apaga_o_cofre_dela_e_derruba_o_login()
    {
        await EnableAsync();
        await CreateAccountAsync("edu");
        var edu = await LoginAsync("edu");
        var accounts = await _local.GetFromJsonAsync<JsonElement>("/api/accounts", Ct);
        var id = accounts.EnumerateArray().Single(a => a.GetProperty("username").GetString() == "edu").GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NoContent, (await _local.DeleteAsync($"/api/accounts/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await edu.GetAsync("/api/pokemon")).StatusCode);
        // Os arquivos dos Pokémon de demonstração continuam: o cofre do dono também os usa.
        var local = await _local.GetFromJsonAsync<JsonElement>("/api/pokemon", Ct);
        var first = local.EnumerateArray().First().GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await _local.GetAsync($"/api/pokemon/{first}/file", Ct)).StatusCode);
    }

    [Fact]
    public async Task Pela_rede_assistente_worker_e_modo_servidor_ficam_de_fora_e_contas_so_para_admin()
    {
        await EnableAsync();
        await CreateAccountAsync("fabi");
        await CreateAccountAsync("gil", role: "admin");
        var fabi = await LoginAsync("fabi");

        foreach (var path in new[] { "/api/setup", "/api/setup/report", "/api/worker", "/api/server" })
        {
            var response = await fabi.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("local_only", await response.Content.ReadAsStringAsync(Ct));
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await fabi.PostAsync("/api/setup/keys", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fabi.PutAsync("/api/engines/selected", new { id = "fake" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fabi.PutAsync("/api/settings/trade", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fabi.PostAsync("/api/app/close", new { })).StatusCode);

        var accounts = await fabi.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Forbidden, accounts.StatusCode);
        Assert.Contains("admin_only", await accounts.Content.ReadAsStringAsync(Ct));

        // Admin pela rede gerencia contas, mas também não mexe no assistente.
        var gil = await LoginAsync("gil");
        Assert.Equal(HttpStatusCode.OK, (await gil.GetAsync("/api/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gil.GetAsync("/api/setup")).StatusCode);

        // Pela rede não há janela nativa (os diálogos abririam no PC).
        var health = await fabi.GetJsonAsync("/api/health");
        Assert.False(health.GetProperty("window").GetBoolean());
    }

    [Fact]
    public async Task Conta_do_dono_nao_pode_ser_apagada_nem_rebaixada()
    {
        var accounts = await _local.GetFromJsonAsync<JsonElement>("/api/accounts", Ct);
        var owner = accounts.EnumerateArray().Single(a => a.GetProperty("isOwner").GetBoolean());
        Assert.Equal("admin", owner.GetProperty("role").GetString());
        Assert.False(owner.GetProperty("canLogin").GetBoolean());
        var id = owner.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.BadRequest, (await _local.DeleteAsync($"/api/accounts/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _local.PutAsJsonAsync($"/api/accounts/{id}", new { role = "user" }, Ct)).StatusCode);

        // Com nome e senha, o dono entra pela rede e vê o próprio cofre.
        var named = await _local.PutAsJsonAsync($"/api/accounts/{id}", new { username = "dono", password = "senha-do-dono" }, Ct);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        await EnableAsync();
        var dono = await LoginAsync("dono", "senha-do-dono");
        var local = await _local.GetFromJsonAsync<JsonElement>("/api/pokemon", Ct);
        Assert.Equal(local.GetArrayLength(), (await dono.GetJsonAsync("/api/pokemon")).GetArrayLength());
    }

    [Fact]
    public async Task Nome_de_conta_repetido_ou_senha_curta_sao_recusados()
    {
        await CreateAccountAsync("hana");
        var repeated = await _local.PostAsJsonAsync("/api/accounts", new { username = "HANA", password = "senha-forte-1" }, Ct);
        Assert.Contains("username_taken", await repeated.Content.ReadAsStringAsync(Ct));
        var shortPassword = await _local.PostAsJsonAsync("/api/accounts", new { username = "ivo", password = "123" }, Ct);
        Assert.Contains("password_invalid", await shortPassword.Content.ReadAsStringAsync(Ct));
        var badName = await _local.PostAsJsonAsync("/api/accounts", new { username = "a b", password = "senha-forte-1" }, Ct);
        Assert.Contains("username_invalid", await badName.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Interface_da_placa_wifi_das_trocas_fica_bloqueada()
    {
        Assert.Equal(HttpStatusCode.OK, (await _local.PutAsJsonAsync("/api/setup/state", new { radio = "wifi" }, Ct)).StatusCode);
        var status = await _local.GetFromJsonAsync<JsonElement>("/api/server", Ct);
        var interfaces = status.GetProperty("interfaces").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!);
        Assert.True(interfaces["wlan0"].GetProperty("blocked").GetBoolean());
        Assert.False(interfaces["eth0"].GetProperty("blocked").GetBoolean());

        var response = await _local.PutAsJsonAsync("/api/server", new { enabled = true, @interface = "wlan0", port = LanPort }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("interface_is_radio", await response.Content.ReadAsStringAsync(Ct));

        // Com o ESP32, qualquer interface serve.
        await _local.PutAsJsonAsync("/api/setup/state", new { radio = "esp32" }, Ct);
        status = await _local.GetFromJsonAsync<JsonElement>("/api/server", Ct);
        Assert.All(status.GetProperty("interfaces").EnumerateArray(), i => Assert.False(i.GetProperty("blocked").GetBoolean()));
    }

    [Fact]
    public async Task Porta_invalida_e_recusada()
    {
        var response = await _local.PutAsJsonAsync("/api/server", new { enabled = true, @interface = "eth0", port = 80 }, Ct);
        Assert.Contains("port_invalid", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Fila_uma_troca_por_vez_com_a_posicao_visivel_e_o_recebido_no_cofre_de_quem_trocou()
    {
        var fake = _app.Services.GetRequiredService<FakeEngineOptions>();
        fake.SpeedFactor = 1; // a troca do dono demora o bastante para a Júlia entrar na fila
        await EnableAsync();
        await CreateAccountAsync("julia");
        var julia = await LoginAsync("julia");

        var localParty = (await _local.GetFromJsonAsync<JsonElement>("/api/pokemon", Ct)).EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()).ToArray();
        var juliaParty = (await julia.GetJsonAsync("/api/pokemon")).EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()).ToArray();

        var first = await _local.PostAsJsonAsync("/api/trades", TradeBody(localParty), Ct);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var localSession = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var second = await julia.PostAsync("/api/trades", TradeBody(juliaParty));
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var queued = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(queued.GetProperty("queued").GetBoolean());
        Assert.Equal(1, queued.GetProperty("position").GetInt32());

        // Um pedido por conta.
        Assert.Equal(HttpStatusCode.Conflict, (await julia.PostAsync("/api/trades", TradeBody(juliaParty))).StatusCode);

        // Todos veem a fila; cada um vê só a própria troca.
        var queue = await julia.GetJsonAsync("/api/trades/queue");
        var entries = queue.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].GetProperty("isHost").GetBoolean());
        Assert.Equal(0, entries[0].GetProperty("position").GetInt32());
        Assert.False(entries[0].GetProperty("mine").GetBoolean());
        Assert.Equal("julia", entries[1].GetProperty("name").GetString());
        Assert.True(entries[1].GetProperty("mine").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await julia.GetAsync("/api/trades/current")).StatusCode);

        // Quem não pediu não cancela a troca dos outros.
        Assert.Equal(HttpStatusCode.Forbidden, (await julia.PostAsync("/api/trades/current/cancel", new { })).StatusCode);

        // O dono cancela a dele; a vez da Júlia chega e a troca dela começa sozinha.
        fake.SpeedFactor = 0;
        await _local.PostAsync("/api/trades/current/cancel", null, Ct);
        JsonElement juliaSession = default;
        for (var i = 0; i < 200; i++)
        {
            var current = await julia.GetAsync("/api/trades/current");
            if (current.StatusCode == HttpStatusCode.OK)
            {
                juliaSession = await current.Content.ReadFromJsonAsync<JsonElement>(Ct);
                if (juliaSession.GetProperty("status").GetString() != "running")
                    break;
            }
            await Task.Delay(50, Ct);
        }
        Assert.Equal("completed", juliaSession.GetProperty("status").GetString());
        Assert.NotEqual(localSession.GetProperty("id").GetString(), juliaSession.GetProperty("id").GetString());

        // O recebido foi para o cofre da Júlia, e o histórico também.
        var received = juliaSession.GetProperty("receivedPokemonIds")[0].GetString();
        Assert.Equal(HttpStatusCode.OK, (await julia.GetAsync($"/api/pokemon/{received}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _local.GetAsync($"/api/pokemon/{received}", Ct)).StatusCode);
        Assert.Single((await julia.GetJsonAsync("/api/trades")).EnumerateArray());
        var localHistory = await _local.GetFromJsonAsync<JsonElement>("/api/trades", Ct);
        Assert.All(localHistory.EnumerateArray(), t => Assert.Equal(localSession.GetProperty("id").GetString(), t.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Sair_da_fila()
    {
        var fake = _app.Services.GetRequiredService<FakeEngineOptions>();
        fake.SpeedFactor = 1;
        await EnableAsync();
        await CreateAccountAsync("kiko");
        var kiko = await LoginAsync("kiko");
        var localParty = (await _local.GetFromJsonAsync<JsonElement>("/api/pokemon", Ct)).EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()).ToArray();
        var kikoParty = (await kiko.GetJsonAsync("/api/pokemon")).EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToArray();

        await _local.PostAsJsonAsync("/api/trades", TradeBody(localParty), Ct);
        var queued = await (await kiko.PostAsync("/api/trades", TradeBody(kikoParty))).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var entry = queued.GetProperty("entryId").GetString();

        Assert.Equal(HttpStatusCode.NoContent, (await kiko.DeleteAsync($"/api/trades/queue/{entry}")).StatusCode);
        var queue = await kiko.GetJsonAsync("/api/trades/queue");
        Assert.Single(queue.GetProperty("entries").EnumerateArray());

        fake.SpeedFactor = 0;
        await _local.PostAsync("/api/trades/current/cancel", null, Ct);
    }

    private static object TradeBody(string?[] party) => new
    {
        party,
        offeredSlots = new[] { 1 },
        game = "fire_red",
        trainer = new { name = "TESTE", tid = 1, sid = 2 },
        simulation = new { scenario = "success" },
    };

    private sealed class FakeInterfaces(params NetworkInterfaceInfo[] list) : INetworkInterfaces
    {
        public IReadOnlyList<NetworkInterfaceInfo> List() => list;
    }

    /// <summary>Sem Kestrel no TestServer: só troca os hosts aceitos, como o listener real.</summary>
    private sealed class FakeLanListener(LanConfigSource config, ServerOptions options) : ILanListener
    {
        public Task<bool> StartAsync(IPAddress address, int port, CancellationToken ct)
        {
            config.Apply(new Dictionary<string, string?> { ["AllowedHosts"] = $"{options.AllowedHosts};{address}" });
            return Task.FromResult(true);
        }

        public Task StopAsync(CancellationToken ct)
        {
            config.Apply(new Dictionary<string, string?>());
            return Task.CompletedTask;
        }
    }

    /// <summary>Requisições com X-Test-Lan "chegam" pelo endereço da rede.</summary>
    private sealed class LanMarker : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Lan", out var remote))
                {
                    context.Connection.LocalIpAddress = IPAddress.Parse(LanHost);
                    context.Connection.LocalPort = LanPort;
                    context.Connection.RemoteIpAddress = IPAddress.Parse(remote.ToString());
                }
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>Cliente "do celular": guarda o cookie de login e manda o token anti-CSRF.</summary>
    private sealed class LanClient
    {
        public HttpClient Client { get; }
        private string? _cookie;
        private string? _csrf;

        public LanClient(HttpClient client, string remote)
        {
            Client = client;
            Client.BaseAddress = new Uri($"http://{LanHost}:{LanPort}/");
            Client.DefaultRequestHeaders.Add("X-Test-Lan", remote);
        }

        public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null, true, null);
        public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null, true, null);
        public Task<HttpResponseMessage> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body, true, null);

        public Task<HttpResponseMessage> PostAsync(string path, object body, bool csrf = true, string? origin = null) =>
            SendAsync(HttpMethod.Post, path, body, csrf, origin);

        public async Task<JsonElement> GetJsonAsync(string path)
        {
            var response = await GetAsync(path);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool csrf, string? origin)
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            if (_cookie is not null)
                request.Headers.Add("Cookie", _cookie);
            if (csrf && _csrf is not null)
                request.Headers.Add(SessionAuthMiddleware.CsrfHeaderName, _csrf);
            if (origin is not null)
                request.Headers.Add("Origin", origin);
            var response = await Client.SendAsync(request);
            if (path == "/api/auth/login" && response.IsSuccessStatusCode)
            {
                _cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
                _csrf = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrf").GetString();
            }
            return response;
        }
    }
}

/// <summary>O endpoint da rede de verdade no Kestrel: liga e desliga com o app aberto (127.0.0.2 faz o papel da rede).</summary>
public sealed class KestrelServerModeTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private WebApplication _app = null!;
    private string _dataDir = null!;

    public async ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("traderock-kestrel-").FullName;
        _app = TradeRockServer.Create(["--data-dir", _dataDir, "--no-window"], _token, b =>
            b.Services.AddSingleton<INetworkInterfaces>(new LoopbackAlias()));
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
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
    public async Task Endpoint_da_rede_liga_e_desliga_sem_reiniciar()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = FreePort();
        using var local = new HttpClient { BaseAddress = TradeRockServer.GetListeningUri(_app) };
        local.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
        using var lan = new HttpClient { BaseAddress = new Uri($"http://127.0.0.2:{port}/") };

        await Assert.ThrowsAsync<HttpRequestException>(() => lan.GetAsync("/api/health", ct));

        var on = await local.PutAsJsonAsync("/api/server", new { enabled = true, @interface = "lo-test", port }, ct);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await lan.GetAsync("/api/health", ct)).StatusCode); // host aceito
        Assert.Equal(HttpStatusCode.Unauthorized, (await lan.GetAsync("/api/vault", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/api/vault", ct)).StatusCode); // a janela continua

        var off = await local.PutAsJsonAsync("/api/server", new { enabled = false }, ct);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        using var fresh = new HttpClient { BaseAddress = lan.BaseAddress };
        await Assert.ThrowsAsync<HttpRequestException>(() => fresh.GetAsync("/api/health", ct));
    }

    [Fact]
    public async Task Porta_ocupada_avisa_e_nao_liga()
    {
        var ct = TestContext.Current.CancellationToken;
        using var busy = new TcpListener(IPAddress.Parse("127.0.0.2"), 0);
        busy.Start();
        var port = ((IPEndPoint)busy.LocalEndpoint).Port;
        using var local = new HttpClient { BaseAddress = TradeRockServer.GetListeningUri(_app) };
        local.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);

        var on = await local.PutAsJsonAsync("/api/server", new { enabled = true, @interface = "lo-test", port }, ct);
        Assert.Equal(HttpStatusCode.Conflict, on.StatusCode);
        Assert.Contains("bind_failed", await on.Content.ReadAsStringAsync(ct));
        var status = await local.GetFromJsonAsync<JsonElement>("/api/server", ct);
        Assert.False(status.GetProperty("enabled").GetBoolean());
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Parse("127.0.0.2"), 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class LoopbackAlias : INetworkInterfaces
    {
        public IReadOnlyList<NetworkInterfaceInfo> List() => [new("lo-test", IPAddress.Parse("127.0.0.2"), false)];
    }
}
