using System.Text.Json.Nodes;
using TradeRock.Core.Activities;
using TradeRock.Core.Games;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>As outras atividades pelo pokeldn_trade.py (ver o cabeçalho dele), com o ESP32 ou a placa Wi-Fi (D26).</summary>
public static class PokeldnActivity
{
    /// <summary>Quanto tempo o presente do Sword/Shield fica no ar (o console só ouve o anúncio).</summary>
    public const int GiftSeconds = 300;

    public static bool Supports(GameFamily family, ActivityKind kind) => kind switch
    {
        ActivityKind.Gift => GiftCatalog.Extensions.ContainsKey(family),
        ActivityKind.Raid => RaidKinds.Game.Values.Contains(family),
        ActivityKind.Circle => family == GameFamily.Sv,
        _ => false,
    };

    /// <param name="radio">"esp32:&lt;porta&gt;", "lan" (raids do Sword/Shield) ou null = placa Wi-Fi.</param>
    public static JsonObject Request(ActivityRequest request, string? radio, string? keysPath)
    {
        if (request.Kind == ActivityKind.Raid)
            return RaidRequest(request, radio, keysPath);
        if (request.Kind == ActivityKind.Circle)
            return CircleRequest(request, radio, keysPath);
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

    private static JsonObject CircleRequest(ActivityRequest request, string? radio, string? keysPath)
    {
        var c = request.Circle ?? throw new ArgumentException("Falta o Union Circle.", nameof(request));
        var json = new JsonObject { ["kind"] = c.Kind, ["code"] = c.Code };
        if (c.Kind == "quests")
        {
            json["task"] = c.Task;
            if (c.Bp is { } bp)
                json["bp"] = bp;
        }
        else
        {
            json["spawn"] = new JsonObject { ["species"] = c.Species, ["level"] = c.Level, ["shiny"] = c.Shiny, ["tera"] = c.Tera };
        }
        var (tid, sid) = TrainerIds.Display(request.GameInfo, request.Trainer.Tid, request.Trainer.Sid ?? 0);
        return new JsonObject
        {
            ["activity"] = "circle",
            ["game"] = request.GameInfo.Key,
            ["circle"] = json,
            ["trainer"] = new JsonObject { ["name"] = request.Trainer.Name, ["tid"] = tid, ["sid"] = sid, ["language"] = 2, ["gender"] = 0 },
            ["keys"] = keysPath,
            ["radio"] = radio,
        };
    }

    private static JsonObject RaidRequest(ActivityRequest request, string? radio, string? keysPath)
    {
        var game = request.GameInfo;
        var raid = request.Raid ?? throw new ArgumentException("Falta a raid.", nameof(request));
        string? Mon(byte[]? data) => data is null ? null : Convert.ToBase64String(PokeldnOffer.Bytes(data, game.Family));
        var json = new JsonObject { ["kind"] = raid.Kind };
        void Put(string name, JsonNode? value)
        {
            if (value is not null)
                json[name] = value;
        }
        Put("boss", Mon(raid.Boss));
        Put("offer", Mon(raid.Offer));
        Put("pokemon", Mon(raid.Pokemon));
        Put("event", raid.Event);
        Put("eventBoss", raid.EventBoss);
        if (raid.Gigantamax)
            json["gigantamax"] = true;
        if (raid.Kind == RaidKinds.MaxRaidJoin)
            json["move"] = raid.Move;
        Put("legendary", raid.Legendary);
        Put("code", raid.Code);
        Put("version", raid.Version);
        Put("map", raid.Map);
        Put("progress", raid.Progress);
        Put("content", raid.Content);
        Put("seed", raid.Seed);
        Put("group", raid.Group);
        if (raid.Kind == RaidKinds.TeraRaidHost)
            json["players"] = raid.Players;
        var (tid, sid) = TrainerIds.Display(game, request.Trainer.Tid, request.Trainer.Sid ?? 0);
        return new JsonObject
        {
            ["activity"] = "raid",
            ["game"] = game.Key,
            ["raid"] = json,
            ["trainer"] = new JsonObject { ["name"] = request.Trainer.Name, ["tid"] = tid, ["sid"] = sid, ["language"] = 2, ["gender"] = 0 },
            ["keys"] = keysPath,
            ["radio"] = radio,
        };
    }

    /// <summary>Mensagens do pokeldn → eventos da atividade, e o resultado.</summary>
    public sealed class Progress(ActivityRequest request)
    {
        public int Delivered { get; private set; }
        public bool Advertised { get; private set; }
        public bool Battled { get; private set; }
        private readonly HashSet<string> _seen = [];
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
                case "milestone" when message["kind"]?.GetValue<string>() is "lobby" or "battle" or "over" && request.Kind is ActivityKind.Raid or ActivityKind.Circle:
                    var kind = message["kind"]!.GetValue<string>();
                    if (!_seen.Add(kind))
                        return null;
                    Battled |= kind is "battle" or "over";
                    return new ActivityEvent(kind switch
                    {
                        "lobby" => ActivityEventKind.Lobby,
                        "battle" => ActivityEventKind.Battle,
                        _ => ActivityEventKind.Over,
                    }, at);
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
            // Missões do Union Circle: deu certo quando o pokeldn diz que a tarefa terminou.
            if (request.Circle is { Kind: "quests" })
                return _seen.Contains("over") ? ActivityResult.Done
                    : _error is not null ? ActivityResult.Fail(_error, _errorMessage)
                    : cancelled ? ActivityResult.Cancelled : ActivityResult.Fail("circle_not_done");
            // Raid e encontros do Union Circle: deu certo quando começou (o pokeldn não diz quem venceu; cada console
            // luta a própria batalha; os encontros ficam até o console fechar o círculo).
            if (request.Kind is ActivityKind.Raid or ActivityKind.Circle)
                return Battled && _error is null ? ActivityResult.Done
                    : _error is not null ? ActivityResult.Fail(_error, _errorMessage)
                    : cancelled ? ActivityResult.Cancelled : ActivityResult.Fail(request.Kind == ActivityKind.Circle ? "circle_not_started" : "raid_not_started");
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
