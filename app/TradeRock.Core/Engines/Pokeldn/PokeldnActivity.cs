using System.Text.Json.Nodes;
using TradeRock.Core.Activities;
using TradeRock.Core.Games;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>As outras atividades pelo pokeldn_trade.py (ver o cabeçalho dele), com o ESP32 ou a placa Wi-Fi (D26).</summary>
public static class PokeldnActivity
{
    /// <summary>Quanto tempo o presente do Sword/Shield fica no ar (o console só ouve o anúncio).</summary>
    public const int GiftSeconds = 300;

    public static bool Supports(GameFamily family, ActivityKind kind) =>
        kind == ActivityKind.Gift && GiftCatalog.Extensions.ContainsKey(family);

    /// <param name="radio">"esp32:&lt;porta&gt;" ou null = placa Wi-Fi (o worker acrescenta as chaves e a placa).</param>
    public static JsonObject Request(ActivityRequest request, string? radio, string? keysPath)
    {
        var game = request.GameInfo;
        var gift = request.Gift ?? throw new ArgumentException("Falta o presente.", nameof(request));
        var giftJson = new JsonObject { ["mode"] = gift.Mode };
        switch (gift.Mode)
        {
            case "preset":
                giftJson["preset"] = gift.Key;
                break;
            case "event":
                giftJson["event"] = gift.Key;
                break;
            default:
                giftJson["data"] = Convert.ToBase64String(gift.Data ?? []);
                giftJson["extension"] = gift.Extension;
                break;
        }
        var (tid, sid) = TrainerIds.Display(game, request.Trainer.Tid, request.Trainer.Sid ?? 0);
        var json = new JsonObject
        {
            ["activity"] = "gift",
            ["game"] = game.Key,
            ["gift"] = giftJson,
            ["trainer"] = new JsonObject { ["name"] = request.Trainer.Name, ["tid"] = tid, ["sid"] = sid, ["language"] = 2, ["gender"] = 0 },
            ["keys"] = keysPath,
            ["radio"] = radio,
        };
        if (game.Family == GameFamily.Frlg)
            json["version"] = request.Game == Trading.TradeGame.LeafGreen ? "leafgreen" : "firered";
        else
            json["seconds"] = GiftSeconds;
        return json;
    }

    /// <summary>Mensagens do pokeldn → eventos da atividade, e o resultado.</summary>
    public sealed class Progress(ActivityRequest request)
    {
        public int Delivered { get; private set; }
        public bool Advertised { get; private set; }
        private string? _error;
        private string? _errorMessage;

        public ActivityEvent? Map(JsonObject message, DateTimeOffset at)
        {
            switch (message["type"]?.GetValue<string>())
            {
                case "event":
                    return message["kind"]?.GetValue<string>() switch
                    {
                        "starting" => new ActivityEvent(ActivityEventKind.Started, at),
                        "waiting" => new ActivityEvent(ActivityEventKind.Waiting, at),
                        "joined" => new ActivityEvent(ActivityEventKind.Joined, at),
                        "left" => new ActivityEvent(ActivityEventKind.Left, at),
                        _ => null,
                    };
                case "done" when message["kind"]?.GetValue<string>() == "delivery":
                    Delivered++;
                    return new ActivityEvent(ActivityEventKind.Delivered, at);
                case "milestone" when message["kind"]?.GetValue<string>() == "advertising" && !Advertised:
                    Advertised = true;
                    return new ActivityEvent(ActivityEventKind.Advertising, at);
                case "error":
                    _error = message["code"]?.GetValue<string>() ?? "engine_error";
                    _errorMessage = message["message"]?.GetValue<string>();
                    return new ActivityEvent(ActivityEventKind.Error, at, _errorMessage) { ErrorCode = _error };
                default:
                    return null;
            }
        }

        /// <summary>
        /// FRLG: deu certo com a entrega confirmada. Sword/Shield: o console não confirma; deu certo se o presente
        /// ficou no ar, e a pessoa confere o cartão no jogo.
        /// </summary>
        public ActivityResult Result(bool cancelled)
        {
            if (Delivered > 0 || (request.GameInfo.Family == GameFamily.Swsh && Advertised && _error is null))
                return ActivityResult.Done;
            if (_error is not null)
                return ActivityResult.Fail(_error, _errorMessage);
            if (cancelled)
                return ActivityResult.Cancelled;
            return ActivityResult.Fail(request.GameInfo.Family == GameFamily.Frlg ? "gift_not_received" : "engine_error");
        }
    }
}
