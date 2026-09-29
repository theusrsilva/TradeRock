namespace TradeRock.App;

/// <summary>Opções de execução, vindas da linha de comando e da configuração (seção "Server").</summary>
public sealed class ServerOptions
{
    /// <summary>Endereço de escuta. Fora do modo dev, só loopback é aceito (o modo servidor na rede vem depois).</summary>
    public string Bind { get; set; } = "127.0.0.1";

    /// <summary>0 = porta aleatória livre.</summary>
    public int Port { get; set; }

    /// <summary>Só o servidor, sem janela (container, testes, abrir no navegador).</summary>
    public bool NoWindow { get; set; }

    /// <summary>Hosts aceitos no cabeçalho Host (proteção contra DNS rebinding).</summary>
    public string AllowedHosts { get; set; } = "127.0.0.1;localhost";

    /// <summary>Token fixo para o proxy do Vite. Ignorado fora do ambiente Development.</summary>
    public string? DevToken { get; set; }

    /// <summary>Pasta de dados alternativa (dev e testes).</summary>
    public string? DataDir { get; set; }

    /// <summary>Habilita as ferramentas de desenvolvedor do WebView.</summary>
    public bool DevTools { get; set; }

    /// <summary>
    /// Converte flags simples (--no-window, --port 5080, --bind 0.0.0.0, --data-dir X, --dev-tools)
    /// para chaves de configuração "Server:*". Demais argumentos seguem para o ASP.NET Core.
    /// </summary>
    public static string[] NormalizeArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--port"] = "Server:Port",
            ["--bind"] = "Server:Bind",
            ["--data-dir"] = "Server:DataDir",
            ["--allowed-hosts"] = "Server:AllowedHosts",
        };
        var flags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--no-window"] = "Server:NoWindow",
            ["--dev-tools"] = "Server:DevTools",
        };

        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (flags.TryGetValue(arg, out var flagKey))
            {
                result.Add($"--{flagKey}=true");
            }
            else if (map.TryGetValue(arg, out var key) && i + 1 < args.Length)
            {
                result.Add($"--{key}={args[++i]}");
            }
            else
            {
                result.Add(arg);
            }
        }
        return result.ToArray();
    }
}
