using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Setup;

namespace TradeRock.Tests;

/// <summary>Roda o script de instalação de verdade com /bin/sh, sem pkexec e sem root.</summary>
public sealed class ShShell : IPrivilegedShell
{
    public int? ForcedExitCode { get; set; }
    public List<IReadOnlyList<string>> Calls { get; } = [];

    public async Task<(int ExitCode, string Error)> RunAsync(string script, IReadOnlyList<string> args, Func<Stream, Task>? stdin, CancellationToken ct)
    {
        Calls.Add(args);
        if (ForcedExitCode is { } code)
            return (code, "");
        return await ShellRunner.RunAsync("/bin/sh", ["-c", script, "traderock-worker-setup", .. args], stdin, ct);
    }
}

/// <summary>Um worker embutido de mentira, com a mesma forma do que scripts/package-linux.sh monta.</summary>
[SupportedOSPlatform("linux")]
public sealed class FakePayload : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("traderock-appimage-").FullName;
    public string Payload => Path.Combine(Root, "payload");
    public string FsRoot => Path.Combine(Root, "fs");

    public FakePayload(string id = "v1")
    {
        Directory.CreateDirectory(Path.Combine(Payload, "python", "bin"));
        Directory.CreateDirectory(FsRoot);
        var launcher = Path.Combine(Payload, "traderock-worker");
        File.WriteAllText(launcher, "#!/bin/sh\n");
        File.SetUnixFileMode(launcher, (UnixFileMode)0b111_101_101); // 755
        File.WriteAllText(Path.Combine(Payload, "python", "bin", "python3.12"), "py");
        File.CreateSymbolicLink(Path.Combine(Payload, "python", "bin", "python3"), "python3.12");
        File.WriteAllText(Path.Combine(Payload, BundledWorker.PolicyFile), "<policyconfig/>");
        SetId(id);
    }

    public void SetId(string id) => File.WriteAllText(Path.Combine(Payload, BundledWorker.IdFile), id + "\n");

    public string Installed(string relative) => Path.Combine(FsRoot, WorkerLocator.AppImage.Prefix.TrimStart('/'), relative);
    public string Policy => Path.Combine(FsRoot, WorkerLocator.AppImage.Policy.TrimStart('/'));

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}

[SupportedOSPlatform("linux")]
public sealed class BundledWorkerTests
{
    [Fact]
    public async Task Instala_atualiza_e_remove_com_o_script_real()
    {
        using var p = new FakePayload();
        var shell = new ShShell();
        var bundled = new BundledWorker(shell, p.Payload, p.FsRoot);
        Assert.Equal(new BundledWorkerStatus(true, false, false), bundled.Status());

        await bundled.InstallAsync(CancellationToken.None);
        Assert.Equal(new BundledWorkerStatus(true, true, true), bundled.Status());
        Assert.Equal("<policyconfig/>", File.ReadAllText(p.Policy));
        Assert.False(File.Exists(p.Installed(BundledWorker.PolicyFile))); // a política foi para o lugar dela
        Assert.Equal("python3.12", new FileInfo(p.Installed("python/bin/python3")).LinkTarget);
        var mode = File.GetUnixFileMode(p.Installed("traderock-worker"));
        Assert.True(mode.HasFlag(UnixFileMode.OtherExecute));
        Assert.False(mode.HasFlag(UnixFileMode.GroupWrite) || mode.HasFlag(UnixFileMode.OtherWrite));
        Assert.False(File.GetUnixFileMode(p.Policy).HasFlag(UnixFileMode.OtherWrite));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(p.Installed(""))!.TrimEnd('/') + "/..", ".traderock-new.*"));

        // O AppImage novo traz outro worker: fica desatualizado até instalar de novo.
        p.SetId("v2");
        Assert.Equal(new BundledWorkerStatus(true, true, false), bundled.Status());
        await bundled.InstallAsync(CancellationToken.None);
        Assert.True(bundled.Status().UpToDate);

        await bundled.UninstallAsync(CancellationToken.None);
        Assert.Equal(new BundledWorkerStatus(true, false, false), bundled.Status());
        Assert.False(File.Exists(p.Policy));
        Assert.Equal(["uninstall", Path.Combine(p.FsRoot, "usr/local/lib/traderock"), p.Policy], shell.Calls[^1]);
    }

    [Fact]
    public async Task Worker_incompleto_nao_substitui_o_instalado()
    {
        using var p = new FakePayload();
        var bundled = new BundledWorker(new ShShell(), p.Payload, p.FsRoot);
        await bundled.InstallAsync(CancellationToken.None);

        File.Delete(Path.Combine(p.Payload, "traderock-worker"));
        p.SetId("v2");
        var e = await Assert.ThrowsAsync<WorkerException>(() => bundled.InstallAsync(CancellationToken.None));
        Assert.Equal("worker_install_failed", e.Code);
        Assert.Contains("worker incompleto", e.Message);
        Assert.True(File.Exists(p.Installed("traderock-worker"))); // o antigo continua lá
        Assert.Equal("v1", File.ReadAllText(p.Installed(BundledWorker.IdFile)).Trim());
    }

    [Theory]
    [InlineData(126, "auth_cancelled")]
    [InlineData(127, "not_authorized")]
    [InlineData(1, "worker_install_failed")]
    public async Task Codigos_do_pkexec(int exitCode, string code)
    {
        using var p = new FakePayload();
        var bundled = new BundledWorker(new ShShell { ForcedExitCode = exitCode }, p.Payload, p.FsRoot);
        Assert.Equal(code, (await Assert.ThrowsAsync<WorkerException>(() => bundled.InstallAsync(CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Sem_worker_embutido()
    {
        var bundled = new BundledWorker(new ShShell(), null);
        Assert.Equal(BundledWorkerStatus.None, bundled.Status());
        Assert.Equal("worker_not_bundled", (await Assert.ThrowsAsync<WorkerException>(() => bundled.InstallAsync(CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Script_recusa_pastas_estranhas()
    {
        foreach (var args in new[] { new[] { "uninstall", "/", "/x.policy" }, ["uninstall", "/usr/local/lib/traderock", "/etc/passwd"], ["format", "/a/traderock", "/a.policy"] })
        {
            var (code, _) = await ShellRunner.RunAsync("/bin/sh", ["-c", BundledWorker.Script, "t", .. args], null, CancellationToken.None);
            Assert.Equal(64, code);
        }
    }

    [Fact]
    public void Acha_o_worker_embutido_ao_lado_do_app()
    {
        using var p = new FakePayload();
        var app = Directory.CreateDirectory(Path.Combine(p.Root, "app")).FullName;
        Directory.Move(p.Payload, Path.Combine(p.Root, "worker-payload"));
        Assert.Equal(Path.Combine(p.Root, "worker-payload"), BundledWorker.FindPayload(app));
        Assert.Null(BundledWorker.FindPayload(Directory.CreateDirectory(Path.Combine(app, "sub")).FullName));
    }

    [Fact]
    public void Verificacao_do_worker_no_appimage()
    {
        var linux = SetupScenarios.All["linux-compatible"] with { SimulatedScenario = null };
        var catalog = HardwareCatalog.Embedded;
        string Code(BundledWorkerStatus? b, WorkerInstall? w)
        {
            var system = linux with { BundledWorker = b, Worker = w };
            var state = new SetupState { Radio = "wifi", Phy = "phy1" };
            return SetupChecks.Evaluate(system, RadioAdvisor.Advise(system, catalog), state, null, catalog).Single(c => c.Id == "worker").Code;
        }
        var appimage = new WorkerInstall("appimage", "/usr/local/lib/traderock/traderock-worker", true, true, true, true);
        var release = appimage with { Flavor = "release", Launcher = "/usr/lib/traderock/traderock-worker" };

        Assert.Equal("worker_appimage_missing", Code(new(true, false, false), null));
        Assert.Equal("worker_appimage_outdated", Code(new(true, true, false), appimage));
        Assert.Equal("worker_installed", Code(new(true, true, true), appimage));
        Assert.Equal("worker_installed", Code(new(true, false, false), release)); // o .deb também serve
        Assert.Equal("worker_missing", Code(null, null));
    }

    [Fact]
    public void AppImage_prefere_a_propria_copia()
    {
        var root = Directory.CreateTempSubdirectory("traderock-locator-").FullName;
        try
        {
            foreach (var c in WorkerLocator.Candidates)
            {
                var launcher = Path.Combine(root, c.Launcher.TrimStart('/'));
                Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
                File.WriteAllText(launcher, "");
            }
            Assert.Equal("release", WorkerLocator.Find(root, writable: _ => false)!.Flavor);
            Assert.Equal("appimage", WorkerLocator.Find(root, writable: _ => false, preferAppImage: true)!.Flavor);
            Assert.Equal("dev", WorkerLocator.Find(root, preferDev: true, writable: _ => false, preferAppImage: true)!.Flavor);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

/// <summary>Rotas /api/worker com o script real (sem root) e o worker falso.</summary>
[SupportedOSPlatform("linux")]
public sealed class WorkerApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly FakeWorker _worker = new();
    private readonly FakePayload _payload = new();
    private readonly ShShell _shell = new();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-wapi-").FullName;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _app = TradeRockServer.Create(["--data-dir", _dataDir], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<IWorkerLauncher>(_worker);
            b.Services.AddSingleton(new BundledWorker(_shell, _payload.Payload, _payload.FsRoot));
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("http://127.0.0.1/");
        _client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _payload.Dispose();
        try { Directory.Delete(_dataDir, true); } catch (IOException) { }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Instalar_e_remover_pela_api()
    {
        var info = await Json(await _client.GetAsync("/api/worker"));
        Assert.True(info.GetProperty("bundled").GetProperty("available").GetBoolean());
        Assert.False(info.GetProperty("bundled").GetProperty("installed").GetBoolean());

        await Json(await _client.PostAsync("/api/worker/install", null));
        Assert.True(File.Exists(_payload.Installed("traderock-worker")));
        info = await Json(await _client.GetAsync("/api/worker"));
        Assert.True(info.GetProperty("bundled").GetProperty("upToDate").GetBoolean());

        await Json(await _client.PostAsync("/api/worker/uninstall", null));
        Assert.False(File.Exists(_payload.Installed("traderock-worker")));
    }

    [Fact]
    public async Task Senha_cancelada_responde_409()
    {
        _shell.ForcedExitCode = 126;
        var response = await _client.PostAsync("/api/worker/install", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("auth_cancelled", await response.Content.ReadAsStringAsync());
    }
}
