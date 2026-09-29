using System.Runtime.InteropServices;

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

    private static bool TryLoad(string library)
    {
        if (!NativeLibrary.TryLoad(library, out var handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }
}
