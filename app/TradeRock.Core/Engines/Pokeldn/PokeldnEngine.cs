using System.Text.Json.Nodes;
using TradeRock.Core.Esp32;
using TradeRock.Core.Games;
using TradeRock.Core.Setup;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>
/// Motor com o ESP32 no USB e o firmware do pokeldn (Linux e Windows, sem root; D23). Troca com todos os jogos do
/// catálogo, o FRLG inclusive. O TradeRock é o anfitrião: abre a sala com o código de troca e o console, procurando
/// com o mesmo código, entra. Uma chamada = uma sessão do pokeldn; o retry é do coordenador.
/// </summary>
public sealed class PokeldnEngine(SetupService setup, ProdKeys keys, IPokeldnProcess process) : ITradeEngine
{
    public const string EngineId = "esp32";

    /// <summary>Quanto tempo a sala fica aberta esperando o console (o pokeldn fecha sozinho depois).</summary>
    public const int RoomSeconds = 600;

    public string Id => EngineId;

    public bool IsSimulation => false;

    public bool Supports(GameFamily family) => true;

    public bool UsesPokeldn => true;

    public async Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var state = (await setup.StatusAsync(cancellationToken)).State;
        if (state.Radio != RadioAdvisor.Esp32 || state.SerialPort is null)
            return new EngineAvailability(false, "esp32_not_configured");
        if (!process.Available)
            return new EngineAvailability(false, "pokeldn_missing");
        return new EngineAvailability(true);
    }

    public async Task<TradeAttemptResult> RunAttemptAsync(TradeRequest request, Func<TradeEvent, ValueTask> emit, CancellationToken cancellationToken)
    {
        var state = (await setup.StatusAsync(cancellationToken)).State;
        if (state.SerialPort is not { } port)
            return TradeAttemptResult.Fail("esp32_not_configured", "Escolha a porta do ESP32 nas configurações.", retryable: false);
        if (!File.Exists(keys.FilePath))
            return TradeAttemptResult.Fail("keys_missing", "O prod.keys não foi enviado.", retryable: false);

        var tracker = new Progress(request);
        try
        {
            await foreach (var message in process.RunAsync("trade", Request(request, port, keys.FilePath), cancellationToken))
            {
                if (tracker.Map(message, DateTimeOffset.UtcNow) is { } e)
                    await emit(e);
            }
        }
        catch (Esp32Exception e)
        {
            return TradeAttemptResult.Fail(e.Code, e.Message, retryable: false);
        }
        if (cancellationToken.IsCancellationRequested)
            return TradeAttemptResult.Cancelled;
        return tracker.Result();
    }

    /// <summary>O pedido do pokeldn_trade.py (ver o cabeçalho dele).</summary>
    public static JsonObject Request(TradeRequest request, string port, string keysPath)
    {
        var game = request.GameInfo;
        var frlg = game.Family == GameFamily.Frlg;
        // FRLG: o time do anfitrião, oferecidos primeiro e na ordem; nos outros jogos, só as ofertas.
        var order = frlg
            ? request.OfferedSlots.Concat(Enumerable.Range(0, request.Party.Count).Except(request.OfferedSlots)).ToList()
            : request.OfferedSlots.ToList();
        var offers = new JsonArray();
        foreach (var slot in order)
            offers.Add(Convert.ToBase64String(PokeldnOffer.Bytes(request.Party[slot].Data, game.Family)));
        var (tid, sid) = TrainerIds.Display(game, request.Trainer.Tid, request.Trainer.Sid ?? 0);
        var json = new JsonObject
        {
            ["game"] = game.Key,
            ["offers"] = offers,
            ["code"] = request.LinkCode,
            ["tradeBack"] = request.TradeBack,
            ["trainer"] = new JsonObject { ["name"] = request.Trainer.Name, ["tid"] = tid, ["sid"] = sid, ["language"] = 2, ["gender"] = 0 },
            ["keys"] = keysPath,
            ["radio"] = $"esp32:{port}",
            ["seconds"] = RoomSeconds,
        };
        if (frlg)
        {
            json["trades"] = request.Trades;
            json["version"] = request.Game == TradeGame.LeafGreen ? "leafgreen" : "firered";
        }
        return json;
    }

    /// <summary>Mensagens do pokeldn → eventos do TradeRock, e o resultado da sessão.</summary>
    public sealed class Progress(TradeRequest request)
    {
        public bool Joined { get; private set; }
        public int Received { get; private set; }
        private string? _error;
        private string? _errorMessage;

        public TradeEvent? Map(JsonObject message, DateTimeOffset at)
        {
            switch (message["type"]?.GetValue<string>())
            {
                case "event":
                    return message["kind"]?.GetValue<string>() switch
                    {
                        "starting" => new TradeEvent(TradeEventKind.Scanning, at, "Abrindo a sala no ESP32."),
                        "waiting" => new TradeEvent(TradeEventKind.InRoom, at, "Sala aberta: procure a troca no Switch com o mesmo código."),
                        "joined" => Join(at),
                        "left" => new TradeEvent(TradeEventKind.LinkClosed, at, "O Switch saiu da sala."),
                        _ => null,
                    };
                case "trade_done":
                    var index = (message["index"]?.GetValue<int>() ?? 1) - 1;
                    var data = message["data"]?.GetValue<string>() is { } b64 ? Convert.FromBase64String(b64) : null;
                    // "Trocar de volta" (pokeldn --trade-back): cada troca vira duas. Na primeira, o Switch manda o
                    // Pokémon que evolui por troca; na segunda, o app o devolve (ele evolui no Switch) e recebe de
                    // volta o que tinha mandado, que é o que entra no cofre (o mesmo arquivo: não duplica).
                    if (index >= request.Trades * (request.TradeBack ? 2 : 1))
                        return null;
                    if (request.TradeBack && index % 2 == 0)
                        return new TradeEvent(TradeEventKind.Log, at, "O Switch mandou o Pokémon; agora ele volta para evoluir lá.");
                    var trade = request.TradeBack ? index / 2 : index;
                    if (data is null)
                        return new TradeEvent(TradeEventKind.Error, at, "Troca concluída, mas o recebido não apareceu.") { ErrorCode = "received_missing", TradeIndex = trade };
                    Received++;
                    return new TradeEvent(TradeEventKind.TradeDone, at, $"Troca {trade + 1} concluída.") { TradeIndex = trade, ReceivedData = data };
                case "error":
                    _error = message["code"]?.GetValue<string>() ?? "engine_error";
                    _errorMessage = message["message"]?.GetValue<string>();
                    return new TradeEvent(TradeEventKind.Error, at, _errorMessage) { ErrorCode = _error };
                default:
                    return null;
            }
        }

        private TradeEvent Join(DateTimeOffset at)
        {
            Joined = true;
            return new TradeEvent(TradeEventKind.Joined, at, "O Switch entrou na sala.");
        }

        public TradeAttemptResult Result()
        {
            if (Received >= request.Trades)
                return TradeAttemptResult.Completed;
            if (Received > 0)
                return TradeAttemptResult.Fail("trade_incomplete", $"{Received}/{request.Trades}", retryable: false);
            if (_error is not null)
                return TradeAttemptResult.Fail(_error, _errorMessage ?? "", retryable: false);
            // Anfitrião: tentar de novo é reabrir a sala, que já esperou o tempo todo.
            return TradeAttemptResult.Fail(Joined ? "connection_lost" : "console_never_joined", "", retryable: false);
        }
    }
}
