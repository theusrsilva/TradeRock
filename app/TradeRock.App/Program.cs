using Photino.NET;
using TradeRock.App;

internal static class Program
{
    [STAThread] // exigido pelo WebView2 no Windows
    private static int Main(string[] args)
    {
        var app = TradeRockServer.Create(args);
        var options = app.Services.GetRequiredService<ServerOptions>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TradeRock");

        app.StartAsync().GetAwaiter().GetResult();

        if (options.NoWindow)
        {
            // Saída padrão, não o log: a URL contém o token de sessão.
            Console.WriteLine($"TradeRock rodando sem janela. Abra: {TradeRockServer.GetLaunchUri(app)}");
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
            return 0;
        }

        // Sem as bibliotecas do WebView, o Photino aborta o processo sem chance de tratar o erro.
        var missing = WindowPrerequisites.MissingLibraries();
        if (missing.Count > 0)
        {
            logger.LogCritical("Bibliotecas da janela ausentes: {Missing}", string.Join(", ", missing));
            Console.Error.WriteLine(WindowPrerequisites.HelpMessage(missing));
            app.StopAsync().GetAwaiter().GetResult();
            return 1;
        }

        RunWindow(TradeRockServer.GetLaunchUri(app), options.DevTools);

        app.StopAsync().GetAwaiter().GetResult();
        return 0;
    }

    private static void RunWindow(Uri launchUri, bool devTools)
    {
        // LogVerbosity = 0 antes de qualquer chamada: o Photino imprime a URL (com o token) no stdout.
        var window = new PhotinoWindow { LogVerbosity = 0 }
            .SetTitle("TradeRock")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 800)
            .SetMinSize(900, 600)
            .Center()
            .SetDevToolsEnabled(devTools)
            .SetContextMenuEnabled(devTools)
            .Load(launchUri);

        window.WaitForClose();
    }
}
