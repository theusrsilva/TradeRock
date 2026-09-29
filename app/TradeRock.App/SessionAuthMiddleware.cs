using TradeRock.Core;

namespace TradeRock.App;

/// <summary>
/// Só quem conhece o token de sessão fala com o servidor.
/// 1. A janela abre "/?t=TOKEN"; o servidor troca o token por um cookie HttpOnly e redireciona sem ele na URL.
/// 2. Toda requisição seguinte precisa do cookie (ou do cabeçalho X-TradeRock-Token, usado pelo proxy do Vite).
/// </summary>
public sealed class SessionAuthMiddleware(RequestDelegate next, SessionToken token)
{
    public const string CookieName = "traderock_session";
    public const string HeaderName = "X-TradeRock-Token";
    public const string QueryName = "t";

    public async Task InvokeAsync(HttpContext context)
    {
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

        await next(context);
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
