using TradeRock.Core.Storage;

namespace TradeRock.App.Server;

/// <summary>Quem fez a requisição: a janela local (sempre o dono do PC, administrador) ou uma conta pela rede.</summary>
public sealed record Caller(Guid UserId, string? Username, UserRole Role, bool IsLocal, LanSession? Session = null)
{
    public static readonly Caller LocalWindow = new(OwnerScope.LocalOwner, null, UserRole.Admin, IsLocal: true);

    public bool IsAdmin => Role == UserRole.Admin;

    public static Caller Of(HttpContext context) =>
        context.Items[typeof(Caller)] as Caller ?? throw new InvalidOperationException("Requisição sem identificação.");

    public static Caller? Find(HttpContext context) => context.Items[typeof(Caller)] as Caller;
}

/// <summary>O que cada tipo de acesso pode fazer. Tudo que mexe no rádio, nas chaves ou no PC fica só na janela local.</summary>
public static class AccessPolicy
{
    /// <summary>Rotas que não precisam de login pela rede.</summary>
    public static bool IsPublic(HttpRequest request) =>
        !request.Path.StartsWithSegments("/api")
        || request.Path.StartsWithSegments("/api/health")
        || request.Path.StartsWithSegments("/api/auth");

    /// <summary>
    /// Só pela janela local: assistente (prod.keys, placa, firmware), worker, modo servidor, fechar o app,
    /// diálogos nativos (abririam no PC, não no celular), motor e configurações de troca.
    /// </summary>
    public static bool IsLocalOnly(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/api/setup") || path.StartsWithSegments("/api/worker") || path.StartsWithSegments("/api/server")
            || path.StartsWithSegments("/api/app") || path.StartsWithSegments("/api/saves/open")
            || path.StartsWithSegments("/api/vault/export/native"))
            return true;
        return HttpMethods.IsPut(request.Method)
            && (path.StartsWithSegments("/api/engines/selected") || path.StartsWithSegments("/api/settings/trade"));
    }

    public static bool IsAdminOnly(HttpRequest request) => request.Path.StartsWithSegments("/api/accounts");

    public static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
