using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Setup;

namespace TradeRock.App.Api;

/// <summary>
/// Componente de rede (worker) embutido no AppImage: instalar, atualizar e remover. O sistema pede a
/// senha de administrador no diálogo dele (pkexec); o app nunca a vê.
/// </summary>
public static class WorkerEndpoints
{
    public static void MapWorker(this IEndpointRouteBuilder api)
    {
        var worker = api.MapGroup("/worker");

        worker.MapGet("/", (WorkerHost host, BundledWorker bundled) => new
        {
            install = host.Install,
            bundled = bundled.Status(),
            running = host.IsRunning,
        });

        worker.MapPost("/install", (WorkerHost host, BundledWorker bundled, SetupService setup, CancellationToken ct) =>
            RunAsync(host, setup, () => bundled.InstallAsync(ct), ct));

        worker.MapPost("/uninstall", (WorkerHost host, BundledWorker bundled, SetupService setup, CancellationToken ct) =>
            RunAsync(host, setup, () => bundled.UninstallAsync(ct), ct));
    }

    private static async Task<IResult> RunAsync(WorkerHost host, SetupService setup, Func<Task> action, CancellationToken ct)
    {
        try
        {
            // O worker da sessão (se houver) sai antes: ele roda dos arquivos que vão ser trocados.
            await host.StopAsync(ct);
            await action();
            return Results.Ok(SetupEndpoints.ToDto(await setup.StatusAsync(ct)));
        }
        catch (WorkerException e)
        {
            return Results.Conflict(new { error = e.Code, message = e.Message });
        }
    }
}
