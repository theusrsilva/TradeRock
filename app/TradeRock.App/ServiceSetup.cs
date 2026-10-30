using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using TradeRock.App.Server;
using TradeRock.Core;
using TradeRock.Core.Games;
using TradeRock.Core.Builder;
using TradeRock.Core.Engines;
using TradeRock.Core.Engines.Pokeldn;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Esp32;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.App;

public static class ServiceSetup
{
    public static JsonNamingPolicy EnumNaming { get; } = JsonNamingPolicy.SnakeCaseLower;

    public static void AddTradeRock(this IServiceCollection services, AppPaths paths, IConfiguration config)
    {
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(EnumNaming));
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddDbContextFactory<TradeRockDb>(o => o.UseSqlite($"Data Source={paths.DatabasePath}"));
        services.AddSingleton<PokemonStore>();
        services.AddSingleton<VaultStore>();
        services.AddSingleton<TradeRock.Core.Setup.ProdKeys>();
        services.AddSingleton<TradeRock.Core.Setup.ISystemProbe, TradeRock.Core.Setup.RealSystemProbe>();
        services.AddSingleton<TradeRock.Core.Setup.SetupService>();
        services.AddSingleton<TradeRock.Core.Saves.SaveBackups>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<TradeRecorder>();
        services.AddSingleton<ITradeRecorder>(sp => sp.GetRequiredService<TradeRecorder>());
        services.AddSingleton<TradeCoordinator>();
        services.AddSingleton<TradeQueue>();
        services.AddSingleton<WindowBridge>();

        // Worker privilegiado (placa Wi-Fi, só Linux). Em desenvolvimento, a instalação dev
        // (scripts/install-worker.sh dev) vem antes da release.
        var development = string.Equals(config["environment"], "Development", StringComparison.OrdinalIgnoreCase);
        // No AppImage, o worker vem embutido (<app>/../worker-payload) e é instalado na primeira vez.
        var payload = OperatingSystem.IsLinux() ? BundledWorker.FindPayload(AppContext.BaseDirectory) : null;
        services.AddSingleton(new WorkerOptions
        {
            PreferDev = config.GetValue("Engines:PythonLdn:PreferDevWorker", development),
            PreferAppImage = payload is not null,
        });
        services.AddSingleton<IWorkerLauncher, PkexecWorkerLauncher>();
        services.AddSingleton<WorkerHost>();
        services.AddSingleton<IPrivilegedShell, PkexecShell>();
        services.AddSingleton(sp => new BundledWorker(sp.GetRequiredService<IPrivilegedShell>(), payload));

        // ESP32: firmware, placa e troca pelo pokeldn (D23) e o esptool, os dois no Python embutido do worker,
        // rodando como o usuário. Em desenvolvimento, o runtime também vale de build/worker/stage.
        var esp32Runtime = config["Engines:Esp32:Runtime"];
        var devStage = development ? FindDevStage() : null;
        Func<string?> runtime = () => esp32Runtime ?? PythonEsp32Flasher.FindRuntime(payload, development, devStage);
        services.AddSingleton<IPokeldnProcess>(new PokeldnProcess(runtime, Path.Combine(paths.DataDir, "tmp")));
        services.AddSingleton<IEsp32Link, PokeldnEsp32Link>();
        services.AddSingleton<IEsp32Flasher>(sp => new PythonEsp32Flasher(runtime, Path.Combine(paths.DataDir, "tmp")));
        services.AddSingleton<TradeRock.Core.Setup.Esp32Setup>();

        // Motores: o primeiro registrado é o padrão (simulação).
        var fake = new FakeEngineOptions
        {
            SpeedFactor = config.GetValue("Engines:Fake:SpeedFactor", 1.0),
            HostPokemon = DemoHostPokemon,
        };
        services.AddSingleton(fake);
        services.AddSingleton<ITradeEngine, FakeEngine>();
        services.AddSingleton(new PythonLdnEngineOptions
        {
            ApprovalSeconds = config.GetValue("Engines:PythonLdn:ApprovalSeconds", 100),
        });
        services.AddSingleton<ITradeEngine, PythonLdnEngine>();
        services.AddSingleton<ITradeEngine, PokeldnEngine>();
        services.AddSingleton<EngineRegistry>();
    }

    /// <summary>
    /// Modo servidor (Entrega 10): contas, logins pela rede, freio de tentativas e limite de requisições por
    /// endereço da rede (a janela local não tem limite).
    /// </summary>
    public static void AddServerMode(this IServiceCollection services)
    {
        services.AddSingleton<AccountStore>();
        services.AddSingleton<LanSessions>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<INetworkInterfaces, SystemNetworkInterfaces>();
        services.AddSingleton<ILanListener, KestrelLanListener>();
        services.AddSingleton<ServerMode>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.RequestServices.GetRequiredService<ServerMode>().IsLanRequest(context))
                    return RateLimitPartition.GetNoLimiter("local");
                var address = context.Connection.RemoteIpAddress?.ToString() ?? "?";
                return RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = LanRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                });
            });
        });
    }

    public const int LanRequestsPerMinute = 600;

    /// <summary>build/worker/stage do repositório (scripts/build-worker.sh), para rodar do código-fonte.</summary>
    private static string? FindDevStage()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TradeRock.slnx")))
                return Path.Combine(dir.FullName, "build", "worker", "stage");
        return null;
    }

    /// <summary>
    /// O "Switch" simulado manda um Pokémon de demonstração diferente do que foi oferecido,
    /// para a troca fazer sentido na tela. Nos jogos novos (sem Pokémon de demonstração), devolve uma cópia
    /// do oferecido com outro treinador.
    /// </summary>
    private static byte[] DemoHostPokemon(TradeRequest request, int tradeIndex)
    {
        if (request.GameInfo.Family != GameFamily.Frlg)
            return DemoCopy(request.Party[request.OfferedSlots[tradeIndex]].Data, request.GameInfo.Family);
        var offered = Pk3Reader.Read(request.Party[request.OfferedSlots[tradeIndex]].Data).Sha256;
        var others = DemoPokemon.All.Where(d => Pk3Reader.Read(d.Data).Sha256 != offered).ToList();
        var pool = others.Count > 0 ? others : DemoPokemon.All.ToList();
        return pool[tradeIndex % pool.Count].Data;
    }

    private static byte[] DemoCopy(byte[] data, GameFamily family)
    {
        var pk = PokemonReader.ToEntity(data, family);
        pk.OriginalTrainerName = "DEMO";
        pk.PID = (uint)Random.Shared.NextInt64(0, uint.MaxValue);
        pk.RefreshChecksum();
        var result = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(result);
        return result;
    }

    /// <summary>Cria as pastas, aplica as migrações e, na primeira execução, coloca os Pokémon de demonstração.</summary>
    public static async Task InitializeDataAsync(this WebApplication app)
    {
        app.Services.GetRequiredService<AppPaths>().EnsureCreated();

        var dbFactory = app.Services.GetRequiredService<IDbContextFactory<TradeRockDb>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        // Em segundo plano: carregar a base do AutoMod (a 1ª geração fica rápida) e reanalisar a
        // legalidade de quem ainda não tem resultado ou foi analisado por outra versão do PKHeX.
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TradeRock.Startup");
        var pokemonStore = app.Services.GetRequiredService<PokemonStore>();
        var stopping = app.Lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                AutoModEnvironment.EnsureInitialized();
                var count = await pokemonStore.RefreshLegalityAsync(stopping);
                if (count > 0)
                    logger.LogInformation("Legalidade reanalisada para {Count} Pokémon.", count);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && stopping.IsCancellationRequested)
            {
                // app encerrando
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao preparar a análise de legalidade.");
            }
        }, stopping);

        var settings = app.Services.GetRequiredService<SettingsStore>();
        if (!await settings.GetAsync("demo.seeded", false, CancellationToken.None))
        {
            var store = app.Services.GetRequiredService<PokemonStore>();
            foreach (var (name, data) in DemoPokemon.All)
                await store.ImportAsync(data, PokemonOrigin.Demo, name, CancellationToken.None);
            await settings.SetAsync("demo.seeded", true, CancellationToken.None);
        }
    }
}

/// <summary>Motores disponíveis e o escolhido (guardado nas configurações).</summary>
public sealed class EngineRegistry(IEnumerable<ITradeEngine> engines, SettingsStore settings)
{
    public const string SettingKey = "engine.selected";

    public IReadOnlyList<ITradeEngine> All { get; } = engines.ToList();

    public async Task<ITradeEngine> GetSelectedAsync(CancellationToken ct)
    {
        var id = await settings.GetAsync<string?>(SettingKey, null, ct);
        return All.FirstOrDefault(e => e.Id == id) ?? All[0];
    }

    public async Task<bool> SelectAsync(string id, CancellationToken ct)
    {
        if (All.All(e => e.Id != id))
            return false;
        await settings.SetAsync(SettingKey, id, ct);
        return true;
    }
}
