using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TradeRock.App;

/// <summary>Confere se as bibliotecas nativas usadas pelo Photino estão instaladas.</summary>
public static class WindowPrerequisites
{
    // Dependências do Photino.Native no Linux (makefile do photino.native: gtk+-3.0, webkit2gtk-4.1, libnotify).
    private static readonly (string Library, string DebianPackage)[] LinuxLibraries =
    [
        ("libgtk-3.so.0", "libgtk-3-0"),
        ("libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.1-0"),
        ("libnotify.so.4", "libnotify4"),
    ];

    public static IReadOnlyList<string> MissingLibraries(Func<string, bool>? canLoad = null)
    {
        if (!OperatingSystem.IsLinux())
            return []; // No Windows, o WebView2 é verificado pelo próprio instalador (Fase 2).

        canLoad ??= TryLoad;
        return LinuxLibraries.Where(l => !canLoad(l.Library)).Select(l => l.Library).ToList();
    }

    public static string HelpMessage(IReadOnlyList<string> missing)
    {
        var packages = LinuxLibraries.Where(l => missing.Contains(l.Library)).Select(l => l.DebianPackage);
        return "Não foi possível abrir a janela do TradeRock: faltam bibliotecas do sistema ("
            + string.Join(", ", missing) + ").\n"
            + "Debian/Ubuntu: sudo apt install " + string.Join(' ', packages) + "\n"
            + "Fedora: sudo dnf install webkit2gtk4.1 libnotify\n"
            + "Arch: sudo pacman -S webkit2gtk-4.1 libnotify\n"
            + "Ou rode com --no-window e abra o endereço impresso no navegador.";
    }

    /// <summary>Id do WebView2 Runtime "Evergreen" no EdgeUpdate (documentação do WebView2: "Detect if a WebView2 Runtime is already installed").</summary>
    private const string WebView2ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public const string WebView2DownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>Versão do WebView2 instalada (por máquina ou por usuário), ou null se não houver.</summary>
    [SupportedOSPlatform("windows")]
    public static string? WebView2Version()
    {
        foreach (var (hive, path) in new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + WebView2ClientKey),
            (Registry.LocalMachine, @"SOFTWARE\" + WebView2ClientKey),
            (Registry.CurrentUser, @"Software\" + WebView2ClientKey),
        })
        {
            using var key = hive.OpenSubKey(path);
            if (key?.GetValue("pv") is string version && version.Length > 0 && version != "0.0.0.0")
                return version;
        }
        return null;
    }

    public const string WebView2Message =
        "Falta o Microsoft Edge WebView2, que o TradeRock usa para mostrar a janela.\n\n" +
        "O TradeRock vai abrir no seu navegador por enquanto. Para ter a janela, instale o " +
        "\"WebView2 Runtime\" (Evergreen) pela página oficial da Microsoft:\n" + WebView2DownloadPage;

    /// <summary>Caixa de mensagem nativa do Windows (sem depender do WebView2).</summary>
    [SupportedOSPlatform("windows")]
    public static void ShowMessage(string text, string title) => MessageBoxW(IntPtr.Zero, text, title, 0x40 /* MB_ICONINFORMATION */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static bool TryLoad(string library)
    {
        if (!NativeLibrary.TryLoad(library, out var handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }
}
