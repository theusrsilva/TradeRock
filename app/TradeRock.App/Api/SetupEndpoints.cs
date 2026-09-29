using System.Reflection;
using TradeRock.Core.Engines.Worker;
using TradeRock.Core.Setup;

namespace TradeRock.App.Api;

public static class SetupEndpoints
{
    public sealed record SimulationDto(string? Scenario);
    public sealed record CardTestDto(string Phy);

    public static void MapSetup(this IEndpointRouteBuilder api)
    {
        var setup = api.MapGroup("/setup");

        setup.MapGet("/", async (SetupService service, CancellationToken ct) => ToDto(await service.StatusAsync(ct)));

        setup.MapGet("/catalog", (SetupService service) => service.Catalog);

        setup.MapPut("/state", async (SetupStatePatch patch, SetupService service, CancellationToken ct) =>
        {
            if (patch.Radio is not null and not (RadioAdvisor.Wifi or RadioAdvisor.Esp32))
                return Results.BadRequest(new { error = "radio_invalid" });
            await service.UpdateStateAsync(patch, ct);
            return Results.Ok(ToDto(await service.StatusAsync(ct)));
        });

        setup.MapPost("/reset", async (SetupService service, CancellationToken ct) =>
        {
            await service.ResetAsync(ct);
            return Results.Ok(ToDto(await service.StatusAsync(ct)));
        });

        // prod.keys: valida e guarda com permissão 600. A resposta nunca traz valores de chave.
        setup.MapPost("/keys", async (HttpRequest request, ProdKeys keys, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "expected_form" });
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count != 1 || form.Files[0].Length is 0 or > ProdKeys.MaxBytes)
                return Results.BadRequest(new { error = "keys_size" });
            using var buffer = new MemoryStream();
            await form.Files[0].CopyToAsync(buffer, ct);
            var validation = keys.Save(buffer.ToArray());
            return validation.Valid ? Results.Ok(validation) : Results.BadRequest(new { error = "keys_invalid", validation });
        }).DisableAntiforgery();

        setup.MapDelete("/keys", (ProdKeys keys) =>
        {
            keys.Delete();
            return Results.NoContent();
        });

        // Teste real da placa. Se o worker ainda não estiver rodando, o sistema pede a senha de
        // administrador (diálogo do polkit) antes de começar.
        setup.MapPost("/card-test", async (CardTestDto body, SetupService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.RunCardTestAsync(body.Phy, ct);
                return Results.Ok(new { result, status = ToDto(await service.StatusAsync(ct)) });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "phy_invalid" });
            }
            catch (WorkerException e)
            {
                return Results.Conflict(new { error = e.Code, message = e.Message });
            }
        });

        // Configuração do NetworkManager/udev para as interfaces do LDN (feita pelo worker, como root).
        setup.MapPost("/network-config", async (SetupService service, CancellationToken ct) =>
        {
            try
            {
                await service.InstallNetworkConfigAsync(ct);
                return Results.Ok(ToDto(await service.StatusAsync(ct)));
            }
            catch (WorkerException e)
            {
                return Results.Conflict(new { error = e.Code, message = e.Message });
            }
        });

        setup.MapGet("/report", async (SetupService service, CancellationToken ct) =>
        {
            var version = typeof(SetupEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            var report = SetupReport.Build(await service.StatusAsync(ct), version.Split('+')[0]);
            return Results.Text(report, "text/markdown; charset=utf-8");
        });

        // Simulação de cenários: só em desenvolvimento.
        setup.MapGet("/scenarios", async (IHostEnvironment env, SetupService service, CancellationToken ct) =>
            env.IsDevelopment()
                ? Results.Ok(new { scenarios = SetupScenarios.All.Keys, current = await service.SimulationAsync(ct) })
                : Results.NotFound());

        setup.MapPut("/simulation", async (SimulationDto body, IHostEnvironment env, SetupService service, CancellationToken ct) =>
        {
            if (!env.IsDevelopment())
                return Results.NotFound();
            try
            {
                await service.SetSimulationAsync(body.Scenario, ct);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "scenario_invalid" });
            }
            return Results.Ok(ToDto(await service.StatusAsync(ct)));
        });
    }

    /// <summary>Resposta do assistente. Nome de usuário fica de fora (não é necessário na interface).</summary>
    public static object ToDto(SetupStatus s) => new
    {
        system = s.System with { User = s.System.User is { } u ? u with { Name = "" } : null },
        serialGroup = s.System.SerialGroup,
        advice = s.Advice,
        state = s.State,
        keys = s.Keys,
        checks = s.Checks,
        readyToTrade = s.ReadyToTrade,
        catalogUpdatedAt = HardwareCatalog.Embedded.UpdatedAt,
    };
}
