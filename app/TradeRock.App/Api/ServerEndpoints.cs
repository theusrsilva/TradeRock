using System.Net;
using TradeRock.App.Server;
using TradeRock.Core.Storage;

namespace TradeRock.App.Api;

/// <summary>Modo servidor: login pela rede, contas e o liga/desliga (este só na janela local).</summary>
public static class ServerEndpoints
{
    public sealed record LoginDto(string? Username, string? Password);
    public sealed record CreateAccountDto(string? Username, string? Password, UserRole Role = UserRole.User);
    public sealed record ServerModeDto(bool Enabled, string? Interface, int? Port);

    public sealed record MeDto(bool Authenticated, bool Local, Guid? Id, string? Username, UserRole? Role, string? Csrf, bool ServerMode);

    public static void MapServer(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapGet("/me", (HttpContext context, ServerMode server) => Me(Caller.Find(context), server));

        auth.MapPost("/login", async (LoginDto body, HttpContext context, AccountStore accounts, LanSessions sessions,
            LoginThrottle throttle, ServerMode server, CancellationToken ct) =>
        {
            if (!server.IsLanRequest(context))
                return Results.Conflict(new { error = "local_window" });

            var username = AccountStore.NormalizeUsername(body.Username);
            var address = context.Connection.RemoteIpAddress ?? IPAddress.None;
            var wait = throttle.RetryAfter(address, username);
            if (wait > 0)
            {
                context.Response.Headers.RetryAfter = wait.ToString();
                return Results.Json(new { error = "too_many_attempts", retryAfter = wait }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            var user = await accounts.VerifyAsync(username, body.Password ?? "", ct);
            if (user is null)
            {
                throttle.Failed(address, username);
                return Results.Json(new { error = "login_failed" }, statusCode: StatusCodes.Status401Unauthorized);
            }
            throttle.Succeeded(username);

            if (Caller.Find(context)?.Session is { } old)
                sessions.Remove(old.Id);
            var session = sessions.Create(user.Id);
            context.Response.Cookies.Append(SessionAuthMiddleware.LanCookieName, session.Id, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = false, // rede de casa em http (decisão do autor, D19)
                Path = "/",
                IsEssential = true,
            });
            return Results.Ok(Me(new Caller(user.Id, user.Username, user.Role, IsLocal: false, session), server));
        });

        auth.MapPost("/logout", (HttpContext context, LanSessions sessions) =>
        {
            if (Caller.Find(context)?.Session is { } session)
                sessions.Remove(session.Id);
            context.Response.Cookies.Delete(SessionAuthMiddleware.LanCookieName);
            return Results.NoContent();
        });

        // Contas (administradores: a janela local e as contas admin pela rede).
        var accountsGroup = api.MapGroup("/accounts");

        accountsGroup.MapGet("/", (AccountStore accounts, CancellationToken ct) => accounts.ListAsync(ct));

        accountsGroup.MapPost("/", (CreateAccountDto body, AccountStore accounts, CancellationToken ct) =>
            Run(() => accounts.CreateAsync(body.Username ?? "", body.Password ?? "", body.Role, ct)));

        accountsGroup.MapPut("/{id:guid}", (Guid id, AccountStore.AccountPatch body, HttpContext context, AccountStore accounts,
            LanSessions sessions, CancellationToken ct) => Run(async () =>
        {
            var result = await accounts.UpdateAsync(id, body, ct);
            // Senha nova, conta desativada ou papel mudado: os logins abertos dela caem.
            if (body.Password is not null || body.Disabled == true || body.Role is not null)
                sessions.RemoveUser(id);
            return result;
        }));

        accountsGroup.MapDelete("/{id:guid}", (Guid id, AccountStore accounts, LanSessions sessions, CancellationToken ct) => Run(async () =>
        {
            await accounts.DeleteAsync(id, ct);
            sessions.RemoveUser(id);
            return true;
        }));

        // Liga/desliga (só janela local, ver AccessPolicy).
        var serverGroup = api.MapGroup("/server");

        serverGroup.MapGet("/", (ServerMode server, CancellationToken ct) => server.StatusAsync(ct));

        serverGroup.MapPut("/", async (ServerModeDto body, HttpContext context, ServerMode server, AccountStore accounts, CancellationToken ct) =>
        {
            try
            {
                if (!body.Enabled)
                    return Results.Ok(await server.DisableAsync(ct));
                await accounts.EnsureOwnerAsync(ct);
                var status = await server.EnableAsync(body.Interface ?? "", body.Port ?? ServerModeSettings.DefaultPort,
                    context.Connection.LocalPort, ct);
                return Results.Ok(status);
            }
            catch (ServerModeException e)
            {
                return Results.Conflict(new { error = e.Code });
            }
        });

        serverGroup.MapGet("/qr", (ServerMode server) =>
            server.Url is { } url ? Results.Text(ServerMode.QrSvg(url), "image/svg+xml") : Results.NotFound());
    }

    private static MeDto Me(Caller? caller, ServerMode server) => caller is null
        ? new MeDto(false, false, null, null, null, null, server.IsEnabled)
        : new MeDto(true, caller.IsLocal, caller.UserId, caller.Username, caller.Role, caller.Session?.Csrf, server.IsEnabled);

    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try
        {
            var result = await action();
            return result is bool ? Results.NoContent() : Results.Ok(result);
        }
        catch (AccountException e)
        {
            return Results.BadRequest(new { error = e.Code });
        }
    }
}
