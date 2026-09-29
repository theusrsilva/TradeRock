using System.Reflection;

namespace TradeRock.App;

/// <summary>Versão do app (Directory.Build.props, ou -p:Version=... vindo da tag no CI), sem o hash do commit.</summary>
public static class AppVersion
{
    public static string Current { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];
}
