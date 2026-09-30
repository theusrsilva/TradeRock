using Photino.NET;
using TradeRock.App;

internal static class Program
{
    [STAThread] // exigido pelo WebView2 no Windows
    private static int Main(string[] args)
    {
        // Windows: o executável é de janela (sem console). Chamado de um terminal (--version,
        // --no-window), escreve no console de quem chamou.
        if (OperatingSystem.IsWindows())
            WindowsConsole.AttachToParent();

        // Antes de tudo: não cria pasta de dados nem banco (usado pelos pacotes e pelo CI).
        if (args is ["--version"])
        {
            Console.WriteLine($"TradeRock {AppVersion.Current}");
            return 0;
        }

        var app = TradeRockServer.Create(args);
        var options = app.Services.GetRequiredService<ServerOptions>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TradeRock");

        app.StartAsync().GetAwaiter().GetResult();

        if (options.NoWindow)
        {
            // Saída padrão, não o log: a URL contém o token de sessão.
            Console.WriteLine($"TradeRock rodando sem janela. Abra: {TradeRockServer.GetLaunchUri(app)}");
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

        // Windows sem WebView2: avisa e abre no navegador padrão, com o app rodando até o fim da sessão.
        if (OperatingSystem.IsWindows() && WindowPrerequisites.WebView2Version() is null)
        {
            logger.LogWarning("WebView2 ausente: abrindo no navegador.");
            WindowPrerequisites.ShowMessage(WindowPrerequisites.WebView2Message, "TradeRock");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TradeRockServer.GetLaunchUri(app).ToString()) { UseShellExecute = true });
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return 0;
        }

        RunWindow(TradeRockServer.GetLaunchUri(app), options.DevTools, app.Services.GetRequiredService<WindowBridge>());

        app.StopAsync().GetAwaiter().GetResult();
        // Descarta os serviços: o coordenador cancela com segurança qualquer troca que tenha sobrado.
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }

    private static void RunWindow(Uri launchUri, bool devTools, WindowBridge bridge)
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

        // Windows: ícone da janela e da barra de tarefas (o instalador põe o .ico ao lado do .exe).
        var icon = Path.Combine(AppContext.BaseDirectory, "traderock.ico");
        if (OperatingSystem.IsWindows() && File.Exists(icon))
            window.SetIconFile(icon);

        bridge.Attach(window);
        window.WaitForClose();
    }
}

internal static class WindowsConsole
{
    private const int AttachParentProcess = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    public static void AttachToParent()
    {
        if (!AttachConsole(AttachParentProcess))
            return;
        // Reabre as saídas padrão no console recém-ligado.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }
}
