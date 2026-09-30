using System.Security.Cryptography;
using System.Text;
using TradeRock.App.Server;
using TradeRock.Core;
using TradeRock.Core.Storage;

namespace TradeRock.App;

/// <summary>
/// Quem pode falar com o servidor.
///
/// Janela local (127.0.0.1): só quem conhece o token de sessão.
/// 1. A janela abre "/?t=TOKEN"; o servidor troca o token por um cookie HttpOnly e redireciona sem ele na URL.
/// 2. Toda requisição seguinte precisa do cookie (ou do cabeçalho X-TradeRock-Token, usado pelo proxy do Vite).
///
/// Rede (modo servidor): o token da janela não vale. A página abre sem login; a API pede o login de uma conta
/// (cookie próprio), o cabeçalho anti-CSRF em toda alteração e respeita <see cref="AccessPolicy"/>.
/// </summary>
public sealed class SessionAuthMiddleware(RequestDelegate next, SessionToken token, ServerMode serverMode, LanSessions sessions,
    AccountStore accounts)
{
    public const string CookieName = "traderock_session";
    public const string HeaderName = "X-TradeRock-Token";
    public const string QueryName = "t";
    public const string LanCookieName = "traderock_lan";
    public const string CsrfHeaderName = "X-TradeRock-CSRF";

    public async Task InvokeAsync(HttpContext context)
    {
        if (serverMode.IsLanRequest(context))
        {
            await LanAsync(context);
            return;
        }

        var request = context.Request;

        if (request.Query.TryGetValue(QueryName, out var fromQuery))
        {
            if (!token.Matches(fromQuery.ToString()))
            {
                await RejectAsync(context);
                return;
            }

            context.Response.Cookies.Append(CookieName, token.Value, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = false, // loopback em http
                Path = "/",
                IsEssential = true,
            });
            context.Response.Redirect(WithoutTokenQuery(request));
            return;
        }

        var authorized = token.Matches(request.Cookies[CookieName])
            || token.Matches(request.Headers[HeaderName].ToString());

        if (!authorized)
        {
            await RejectAsync(context);
            return;
        }

        context.Items[typeof(Caller)] = Caller.LocalWindow;
        using (OwnerScope.Use(OwnerScope.LocalOwner))
            await next(context);
    }

    private async Task LanAsync(HttpContext context)
    {
        var request = context.Request;

        // Alteração vinda de outra página (outro site aberto no celular): o navegador manda o Origin.
        if (!AccessPolicy.IsSafeMethod(request.Method) && request.Headers.Origin.ToString() is { Length: > 0 } origin
            && !string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase))
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "csrf");
            return;
        }

        var session = sessions.Get(request.Cookies[LanCookieName]);
        UserAccount? user = null;
        if (session is not null)
        {
            user = await accounts.FindAsync(session.UserId, context.RequestAborted);
            if (user is null || user.Disabled || user.PasswordHash is null)
            {
                sessions.Remove(session.Id);
                session = null;
                user = null;
            }
        }

        if (session is not null && user is not null)
        {
            context.Items[typeof(Caller)] = new Caller(user.Id, user.Username, user.Role, IsLocal: false, session);

            if (!AccessPolicy.IsSafeMethod(request.Method) && !CsrfMatches(session, request.Headers[CsrfHeaderName].ToString()))
            {
                await DenyAsync(context, StatusCodes.Status403Forbidden, "csrf");
                return;
            }
        }

        if (AccessPolicy.IsPublic(request))
        {
            using (OwnerScope.Use(user?.Id ?? Guid.Empty))
                await next(context);
            return;
        }

        if (session is null || user is null)
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "login_required");
            return;
        }
        if (AccessPolicy.IsLocalOnly(request))
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "local_only");
            return;
        }
        if (AccessPolicy.IsAdminOnly(request) && user.Role != UserRole.Admin)
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "admin_only");
            return;
        }

        using (OwnerScope.Use(user.Id))
            await next(context);
    }

    private static bool CsrfMatches(LanSession session, string header) =>
        header.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(session.Csrf));

    private static Task DenyAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = code });
    }

    private static string WithoutTokenQuery(HttpRequest request)
    {
        var rest = request.Query
            .Where(kv => kv.Key != QueryName)
            .SelectMany(kv => kv.Value.Select(v => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v ?? "")}"));
        var query = string.Join('&', rest);
        var path = request.PathBase + request.Path;
        if (string.IsNullOrEmpty(path))
            path = "/";
        return query.Length > 0 ? $"{path}?{query}" : path.ToString();
    }

    private static Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            return context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(
            "<!doctype html><meta charset=utf-8><title>TradeRock</title>" +
            "<p>Abra o TradeRock pela janela do aplicativo.</p>");
    }
}
