using System.Diagnostics;
using System.Text.Json.Nodes;
using TradeRock.Core.Engines.Pokeldn;
using TradeRock.Core.Games;

namespace TradeRock.Core.Esp32;

/// <summary>
/// A placa com o firmware do pokeldn (D23), pelo worker/traderock_worker/pokeldn_board.py: o HELLO (firmware e chip)
/// e a varredura do teste com o Switch, sem entrar em sala nenhuma.
/// </summary>
public sealed class PokeldnEsp32Link(IPokeldnProcess process) : IEsp32Link
{
    /// <summary>Canais de 2,4 GHz da varredura, e o tempo em cada um.</summary>
    public static readonly int[] Channels = [1, 6, 11, 2, 3, 4, 5, 7, 8, 9, 10];
    public const double DwellSeconds = 0.5;

    /// <summary>Sala de troca de algum jogo do catálogo.</summary>
    public static bool IsPokemon(ulong communicationId) => GameCatalog.CommunicationIds.ContainsKey(communicationId);

    public Esp32Hello Probe(string port, CancellationToken ct)
    {
        var r = Run(new JsonObject { ["op"] = "hello", ["port"] = port }, ct);
        var target = r["target"]?.GetValue<string>()
            ?? throw new Esp32Exception("esp32_firmware_missing", "A placa respondeu, mas não com o firmware do pokeldn.");
        return new Esp32Hello(target);
    }

    public Esp32ScanResult Scan(string port, string keysPath, TimeSpan limit, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var rooms = new Dictionary<ulong, Esp32Room>();
        var undecryptable = 0;
        var sweeps = 0;
        // Uma varredura por vez até achar uma sala de troca ou acabar o tempo.
        do
        {
            ct.ThrowIfCancellationRequested();
            var r = Run(new JsonObject
            {
                ["op"] = "scan", ["port"] = port, ["keys"] = keysPath,
                ["channels"] = new JsonArray([.. Channels.Select(c => (JsonNode)c)]), ["dwell"] = DwellSeconds,
            }, ct);
            sweeps++;
            undecryptable += r["undecryptable"]?.GetValue<int>() ?? 0;
            foreach (var room in r["rooms"]?.AsArray() ?? [])
            {
                var id = room!["communicationId"]!.GetValue<ulong>();
                rooms[id] = new Esp32Room(id, room["channel"]!.GetValue<int>(), room["protocol"]?.GetValue<int>() ?? 0,
                    room["members"]?.GetValue<int>() ?? 0, room["maximum"]?.GetValue<int>() ?? 0, room["policy"]?.GetValue<int>() ?? 0);
            }
        }
        while (!rooms.Keys.Any(IsPokemon) && watch.Elapsed < limit);
        return new Esp32ScanResult([.. rooms.Values], undecryptable, [], sweeps, watch.Elapsed.TotalSeconds, rooms.Keys.Any(IsPokemon));
    }

    private JsonObject Run(JsonObject request, CancellationToken ct) => Task.Run(async () =>
    {
        JsonObject? result = null;
        await foreach (var message in process.RunAsync("board", request, ct))
        {
            switch (message["type"]?.GetValue<string>())
            {
                case "result":
                    result = message;
                    break;
                case "error":
                    throw new Esp32Exception(message["code"]?.GetValue<string>() ?? "esp32_no_response", message["message"]?.GetValue<string>() ?? "");
            }
        }
        ct.ThrowIfCancellationRequested();
        return result ?? throw new Esp32Exception("esp32_no_response", "A placa não respondeu.");
    }, ct).GetAwaiter().GetResult();
}
