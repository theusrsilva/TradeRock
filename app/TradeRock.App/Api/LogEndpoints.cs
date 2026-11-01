using System.Text;
using TradeRock.Core.Logging;

namespace TradeRock.App.Api;

/// <summary>A aba Logs das Configurações (D28): o log em arquivo do app, já sem chaves nem dados. Só na janela do PC.</summary>
public static class LogEndpoints
{
    public const int MaxLines = 2000;

    public sealed record LogsDto(IReadOnlyList<LogLine> Lines, int KeepDays);

    public static void MapLogs(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/logs");

        group.MapGet("/", (FileLog log, int? lines, bool? problems) =>
            Results.Ok(new LogsDto(log.Tail(Math.Clamp(lines ?? 500, 1, MaxLines), problems ?? false), FileLog.KeepDays)));

        // Os dias guardados num arquivo de texto só, para anexar a um relato de problema.
        group.MapGet("/file", (FileLog log, TimeProvider time) =>
        {
            var text = new StringBuilder();
            foreach (var file in log.Files())
                text.Append(File.ReadAllText(file, Encoding.UTF8));
            return Results.File(Encoding.UTF8.GetBytes(text.ToString()), "text/plain; charset=utf-8",
                $"traderock-log-{time.GetUtcNow():yyyyMMdd-HHmm}.txt");
        });
    }
}
