using TradeRock.App;
using TradeRock.Core;

namespace TradeRock.Tests;

public sealed class CoreTests
{
    [Fact]
    public void Token_gerado_e_unico_e_nao_aparece_no_ToString()
    {
        var a = SessionToken.Generate();
        var b = SessionToken.Generate();

        Assert.NotEqual(a.Value, b.Value);
        Assert.True(a.Matches(a.Value));
        Assert.False(a.Matches(b.Value));
        Assert.False(a.Matches(null));
        Assert.DoesNotContain(a.Value, a.ToString());
    }

    [Fact]
    public void Token_curto_e_rejeitado()
    {
        Assert.Throws<ArgumentException>(() => new SessionToken("curto"));
    }

    [Fact]
    public void Linux_usa_xdg_data_home()
    {
        if (OperatingSystem.IsWindows())
            return;

        var paths = AppPaths.ForCurrentUser(env: name => name == "XDG_DATA_HOME" ? "/tmp/xdg" : null);
        Assert.Equal("/tmp/xdg/traderock", paths.DataDir);
    }

    [Fact]
    public void Linux_ignora_xdg_relativo()
    {
        if (OperatingSystem.IsWindows())
            return;

        var paths = AppPaths.ForCurrentUser(env: name => name == "XDG_DATA_HOME" ? "relativo" : null);
        Assert.EndsWith("/.local/share/traderock", paths.DataDir);
    }

    [Fact]
    public void Pasta_de_chaves_fica_so_para_o_dono()
    {
        if (OperatingSystem.IsWindows())
            return;

        var dir = Directory.CreateTempSubdirectory("traderock-paths-").FullName;
        try
        {
            var paths = new AppPaths(dir);
            paths.EnsureCreated();
            var mode = File.GetUnixFileMode(paths.KeysDir);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, mode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Flags_simples_viram_configuracao()
    {
        var normalized = ServerOptions.NormalizeArgs(["--no-window", "--port", "5080", "--outro=1"]);
        Assert.Equal(["--Server:NoWindow=true", "--Server:Port=5080", "--outro=1"], normalized);
    }
}

public sealed class WindowPrerequisitesTests
{
    [Fact]
    public void Lista_bibliotecas_ausentes_e_pacotes_para_instalar()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var missing = WindowPrerequisites.MissingLibraries(lib => lib != "libwebkit2gtk-4.1.so.0");

        Assert.Equal(["libwebkit2gtk-4.1.so.0"], missing);
        Assert.Contains("sudo apt install libwebkit2gtk-4.1-0", WindowPrerequisites.HelpMessage(missing));
    }
}
