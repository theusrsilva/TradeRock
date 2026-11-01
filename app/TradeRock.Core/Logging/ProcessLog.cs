using Microsoft.Extensions.Logging;

namespace TradeRock.Core.Logging;

/// <summary>A saída de erro de um processo auxiliar (pokeldn, esptool) no log do app, ao terminar (D28).</summary>
public static class ProcessLog
{
    /// <summary>Só o fim da saída: o pokeldn pode escrever muito numa troca longa.</summary>
    public const int MaxLines = 300;

    public static void Write(ILogger? logger, string name, int exitCode, string stderr)
    {
        if (logger is null)
            return;
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = lines.Length > MaxLines ? lines[^MaxLines..] : lines;
        var text = kept.Length == 0 ? "" : "\n" + string.Join('\n', kept);
        if (exitCode == 0)
            logger.LogInformation("{Name} terminou.{Output}", name, text);
        else
            logger.LogWarning("{Name} terminou com o código {Code}.{Output}", name, exitCode, text);
    }
}
