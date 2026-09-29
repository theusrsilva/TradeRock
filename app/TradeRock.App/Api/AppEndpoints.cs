using System.Reflection;
using TradeRock.Core.Pokemon;

namespace TradeRock.App.Api;

public static class AppEndpoints
{
    public static void MapAppEndpoints(this IEndpointRouteBuilder api)
    {
        var version = typeof(AppEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        api.MapGet("/health", (IHostEnvironment env, WindowBridge window) => Results.Ok(new
        {
            app = "TradeRock",
            version,
            os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other",
            environment = env.EnvironmentName,
            window = window.HasWindow,
        }));

        // Chamado pelo frontend depois que o usuário confirma fechar durante uma troca.
        api.MapPost("/app/close", async (WindowBridge window) =>
        {
            await window.CloseAsync();
            return Results.NoContent();
        });

        // Sprites 1–386 do PokeAPI (via engines/frlg-ldn-trade-esp32/assets/sprites), embutidos.
        var assembly = typeof(AppEndpoints).Assembly;
        api.MapGet("/sprites/{species:int}", (int species, HttpContext context) =>
        {
            if (species is < 1 or > Pk3Reader.MaxSpecies)
                return Results.NotFound();
            var stream = assembly.GetManifestResourceStream($"sprites/{species}.png");
            if (stream is null)
                return Results.NotFound();
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.Stream(stream, "image/png");
        });
    }
}
