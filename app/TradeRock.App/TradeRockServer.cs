using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using TradeRock.App.Api;
using TradeRock.App.Server;
using TradeRock.Core;

namespace TradeRock.App;

/// <summary>Monta o servidor ASP.NET Core embutido (API + frontend).</summary>
public static class TradeRockServer
{
    public static WebApplication Create(string[] args, SessionToken? token = null, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = ServerOptions.NormalizeArgs(args),
            ContentRootPath = AppContext.BaseDirectory,
        });

        var options = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
        var isDev = builder.Environment.IsDevelopment();

        if (!isDev && !IsLoopback(options.Bind))
        {
            throw new InvalidOperationException(
                "Fora do modo desenvolvimento, o TradeRock só escuta em 127.0.0.1. " +
                "Para a rede, ligue o \"modo servidor\" nas Configurações (contas com senha).");
        }

        token ??= isDev && !string.IsNullOrEmpty(options.DevToken)
            ? new SessionToken(options.DevToken)
            : SessionToken.Generate();

        builder.Configuration["AllowedHosts"] = options.AllowedHosts;

        // Modo servidor: o endpoint da rede e os hosts aceitos mudam com o app aberto. O Kestrel já
        // recarrega a seção "Kestrel" da configuração (reloadOnChange), e a filtragem de hosts também.
        var lanConfig = new LanConfigSource();
        ((IConfigurationBuilder)builder.Configuration).Add(lanConfig);

        // A URL inicial carrega o token (?t=...). O log de requisições do ASP.NET Core
        // registra a URL completa em Information, então ele fica restrito a avisos.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

        builder.WebHost.ConfigureKestrel(k => k.Listen(ParseAddress(options.Bind), options.Port));

        var paths = AppPaths.ForCurrentUser(options.DataDir);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(token);
        builder.Services.AddSingleton(paths);
        builder.Services.AddTradeRock(paths, builder.Configuration);
        builder.Services.AddSingleton(lanConfig);
        builder.Services.AddServerMode();

        configure?.Invoke(builder);

        var app = builder.Build();
        app.InitializeDataAsync().GetAwaiter().GetResult();

        app.UseRateLimiter();
        app.UseMiddleware<SessionAuthMiddleware>();

        var frontend = FrontendFiles();
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontend });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = frontend });

        MapApi(app);

        // Rotas do React Router caem no index.html; /api desconhecida continua 404.
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var index = frontend.GetFileInfo("index.html");
            context.Response.ContentType = "text/html; charset=utf-8";
            if (!index.Exists)
            {
                await context.Response.WriteAsync(MissingFrontendPage);
                return;
            }
            await using var stream = index.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
        });

        return app;
    }

    private static void MapApi(WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapAppEndpoints();
        api.MapPokemon();
        api.MapTrades();
        api.MapSettings();
        api.MapBuilder();
        api.MapVault();
        api.MapSaves();
        api.MapSetup();
        api.MapWorker();
        api.MapServer();
    }

    /// <summary>Endereço real após o start (a porta 0 vira uma porta livre).</summary>
    public static Uri GetListeningUri(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Servidor sem endereços.");
        var address = addresses.Addresses.First();
        var uri = new Uri(address);
        // "0.0.0.0"/"[::]" não servem para abrir no navegador.
        var host = uri.Host is "0.0.0.0" or "[::]" or "::" ? "127.0.0.1" : uri.Host;
        return new UriBuilder(uri) { Host = host }.Uri;
    }

    public static Uri GetLaunchUri(WebApplication app)
    {
        var token = app.Services.GetRequiredService<SessionToken>();
        var builder = new UriBuilder(GetListeningUri(app)) { Path = "/", Query = $"{SessionAuthMiddleware.QueryName}={token.Value}" };
        return builder.Uri;
    }

    private static IFileProvider FrontendFiles()
    {
        var assembly = typeof(TradeRockServer).Assembly;
        try
        {
            return new ManifestEmbeddedFileProvider(assembly, "wwwroot");
        }
        catch (InvalidOperationException)
        {
            // Build sem o frontend compilado (ex.: backend rodando atrás do Vite em dev).
            return new NullFileProvider();
        }
    }

    private static bool IsLoopback(string bind) =>
        bind is "localhost" || (IPAddress.TryParse(bind, out var ip) && IPAddress.IsLoopback(ip));

    private static IPAddress ParseAddress(string bind) =>
        bind == "localhost" ? IPAddress.Loopback : IPAddress.Parse(bind);

    private const string MissingFrontendPage =
        "<!doctype html><meta charset=utf-8><title>TradeRock</title>" +
        "<h1>TradeRock</h1><p>O frontend não foi compilado neste build.</p>" +
        "<p>Rode <code>npm run build</code> em <code>frontend/</code> ou use o Vite em modo dev.</p>";
}
