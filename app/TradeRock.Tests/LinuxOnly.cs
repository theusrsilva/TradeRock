using System.Runtime.CompilerServices;

namespace TradeRock.Tests;

/// <summary>Teste que depende do Linux (worker/pkexec, /sys, pseudo-terminal): pulado no Windows.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Só no Linux.";
    }
}

/// <inheritdoc cref="LinuxFactAttribute"/>
public sealed class LinuxTheoryAttribute : TheoryAttribute
{
    public LinuxTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Só no Linux.";
    }
}
