using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TradeRock.Core.Logging;

/// <summary>
/// Apaga do log o que não pode sair do PC: sequências hexadecimais longas (chaves do prod.keys, chaves de sessão,
/// dados de Pokémon) e o token de sessão da URL. É a mesma regra do worker (pokeldn_env.redact).
/// </summary>
public static partial class LogRedactor
{
    [GeneratedRegex(@"\b(?:[0-9a-fA-F]{2}[ :]?){16,}\b")]
    private static partial Regex Hex();

    [GeneratedRegex(@"([?&]t=)[^&\s]+")]
    private static partial Regex UrlToken();

    // Base64 longo (Pokémon, presentes, arquivos) não serve para ler o log.
    [GeneratedRegex(@"[A-Za-z0-9+/]{64,}={0,2}")]
    private static partial Regex Base64();

    public static string Redact(string text) =>
        Base64().Replace(UrlToken().Replace(Hex().Replace(text, "[hex]"), "$1[token]"), "[dados]");
}

public sealed record LogLine(DateTimeOffset At, string Level, string Category, string Message);

/// <summary>
/// O log do app em arquivo (D28): um arquivo por dia em logs/, guardado por <see cref="KeepDays"/> dias, para a aba
/// Logs das Configurações. Tudo passa pelo <see cref="LogRedactor"/> antes de ser gravado.
/// </summary>
public sealed class FileLog
{
    public const int KeepDays = 7;
    private const string Prefix = "traderock-";
    private readonly Lock _lock = new();
    private readonly string _dir;
    private readonly TimeProvider _time;

    public FileLog(string dir, TimeProvider? time = null)
    {
        _dir = dir;
        _time = time ?? TimeProvider.System;
        System.IO.Directory.CreateDirectory(dir);
        Prune();
    }

    public void Write(string level, string category, string message)
    {
        var at = _time.GetUtcNow();
        var clean = LogRedactor.Redact(message).Replace("\r", "").Replace("\n", "\n    ");
        var line = $"{at:yyyy-MM-ddTHH:mm:ss.fffZ} {level} {category}: {clean}\n";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(Path.Combine(_dir, $"{Prefix}{at:yyyyMMdd}.log"), line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Sem log não é motivo para parar o app.
            }
        }
    }

    /// <summary>As últimas <paramref name="max"/> linhas (as mais novas no fim), dos arquivos mais recentes.</summary>
    public IReadOnlyList<LogLine> Tail(int max, bool problemsOnly)
    {
        var lines = new List<LogLine>();
        lock (_lock)
        {
            foreach (var file in Files().Reverse())
            {
                var parsed = Parse(File.ReadAllLines(file, Encoding.UTF8))
                    .Where(l => !problemsOnly || l.Level is "WRN" or "ERR")
                    .ToList();
                lines.InsertRange(0, parsed);
                if (lines.Count >= max)
                    break;
            }
        }
        return lines.Count > max ? lines[^max..] : lines;
    }

    /// <summary>Os arquivos de log, do mais antigo ao mais novo.</summary>
    public IEnumerable<string> Files() =>
        System.IO.Directory.EnumerateFiles(_dir, $"{Prefix}*.log").Order(StringComparer.Ordinal);

    private void Prune()
    {
        var limit = $"{Prefix}{_time.GetUtcNow().AddDays(-KeepDays):yyyyMMdd}.log";
        foreach (var file in Files().Where(f => string.CompareOrdinal(Path.GetFileName(f), limit) < 0).ToList())
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static IEnumerable<LogLine> Parse(string[] raw)
    {
        LogLine? current = null;
        var extra = new StringBuilder();
        foreach (var line in raw)
        {
            // Continuação de uma mensagem de várias linhas (ex.: exceção).
            if (line.StartsWith("    ", StringComparison.Ordinal) && current is not null)
            {
                extra.Append('\n').Append(line[4..]);
                continue;
            }
            if (current is not null)
                yield return current with { Message = current.Message + extra };
            extra.Clear();
            current = TryParse(line);
        }
        if (current is not null)
            yield return current with { Message = current.Message + extra };
    }

    private static LogLine? TryParse(string line)
    {
        var parts = line.Split(' ', 3);
        if (parts.Length < 3 || !DateTimeOffset.TryParse(parts[0], out var at))
            return null;
        var colon = parts[2].IndexOf(": ", StringComparison.Ordinal);
        return colon < 0
            ? new LogLine(at, parts[1], "", parts[2])
            : new LogLine(at, parts[1], parts[2][..colon], parts[2][(colon + 2)..]);
    }
}

/// <summary>Leva os logs do app para o <see cref="FileLog"/>: Information do TradeRock, avisos e erros do resto.</summary>
public sealed class FileLoggerProvider(FileLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(log, categoryName);

    public void Dispose()
    {
    }

    private sealed class FileLogger(FileLog log, string category) : ILogger
    {
        private readonly string _short = category.StartsWith("TradeRock.", StringComparison.Ordinal)
            ? category[(category.LastIndexOf('.') + 1)..]
            : category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None
            && logLevel >= (category.StartsWith("TradeRock", StringComparison.Ordinal) ? LogLevel.Information : LogLevel.Warning);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var message = formatter(state, exception);
            if (exception is not null)
                message += $"\n{exception}";
            log.Write(Level(logLevel), _short, message);
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Critical or LogLevel.Error => "ERR",
            LogLevel.Warning => "WRN",
            LogLevel.Information => "INF",
            _ => "DBG",
        };
    }
}
