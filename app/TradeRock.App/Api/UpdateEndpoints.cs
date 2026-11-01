using TradeRock.Core.Updates;

namespace TradeRock.App.Api;

/// <summary>Atualização pela internet (D29), só pela janela do PC.</summary>
public static class UpdateEndpoints
{
    public sealed record AutoDto(bool On);

    public static void MapUpdates(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/update");

        group.MapGet("/", (UpdateService updates, CancellationToken ct) => updates.StatusAsync(ct));

        group.MapPost("/check", async (UpdateService updates, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await updates.CheckAsync(ct));
            }
            catch (UpdateException e)
            {
                return Results.Json(new { error = e.Code, message = e.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapPut("/auto", async (AutoDto body, UpdateService updates, CancellationToken ct) =>
        {
            await updates.SetAutoAsync(body.On, ct);
            return Results.Ok(await updates.StatusAsync(ct));
        });

        group.MapPost("/install", async (UpdateService updates, CancellationToken ct) =>
        {
            try
            {
                return Results.Accepted("/api/update", await updates.StartInstallAsync(ct));
            }
            catch (UpdateException e)
            {
                return Results.Conflict(new { error = e.Code, message = e.Message });
            }
        });
    }
}

/// <summary>Procura a versão nova uns segundos depois de abrir, se a pessoa não desligou (no máximo uma vez por dia).</summary>
public sealed class UpdateAutoCheck(UpdateService updates, IUpdateInstaller installer) : BackgroundService
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Rodando do código (desenvolvimento, testes): só pelo botão.
        if (installer.Kind == InstallKind.None)
            return;
        try
        {
            await Task.Delay(Delay, stoppingToken);
            await updates.AutoCheckAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

/// <summary>Fecha o app depois que o instalador começou: a janela, ou o servidor quando não há janela.</summary>
public sealed class AppExit(WindowBridge window, IHostApplicationLifetime lifetime) : IAppExit
{
    public async Task ExitAsync()
    {
        if (window.HasWindow)
            await window.CloseAsync();
        else
            lifetime.StopApplication();
    }
}
