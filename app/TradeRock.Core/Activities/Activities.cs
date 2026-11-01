using System.Text.Json;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Activities;

/// <summary>Outras atividades do pokeldn além da troca (D26): Mystery Gift, raids e Union Circle.</summary>
public enum ActivityKind
{
    Gift,
    Raid,
    Circle,
}

/// <summary>
/// Union Circle do Scarlet/Violet: o console abre o círculo com um código de 4 dígitos e o TradeRock entra.
/// "quests": cumpre missões do Blueberry por ele (todos os lendários do Snacksworth, ou os BP escolhidos);
/// "encounters": faz aparecer o Pokémon selvagem escolhido em volta do jogador.
/// </summary>
public sealed record CircleChoice(string Kind, string Code, string Task = "snacksworth", int? Bp = null,
    int? Species = null, int Level = 30, bool Shiny = false, int? Tera = null)
{
    public const int MaxBp = 9_999_999;
}

/// <summary>Tipos de raid (pokeldn_trade.py RAIDS): o TradeRock abre a sala (host) ou entra na do console (join).</summary>
public static class RaidKinds
{
    public const string MaxRaidHost = "max-raid-host";
    public const string MaxRaidJoin = "max-raid-join";
    public const string LairHost = "lair-host";
    public const string LairJoin = "lair-join";
    public const string TeraRaidHost = "tera-raid-host";
    public const string TeraRaidJoin = "tera-raid-join";

    public static readonly IReadOnlyDictionary<string, GameFamily> Game = new Dictionary<string, GameFamily>
    {
        [MaxRaidHost] = GameFamily.Swsh, [MaxRaidJoin] = GameFamily.Swsh, [LairHost] = GameFamily.Swsh,
        [LairJoin] = GameFamily.Swsh, [TeraRaidHost] = GameFamily.Sv, [TeraRaidJoin] = GameFamily.Sv,
    };

    /// <summary>Lendários da Dynamax Adventure (catálogo do pokeldn, --lair-legendary).</summary>
    public static readonly int[] LairLegendaries = [144, 145, 146, 150, 243, 244, 245, 249, 250, 380, 381, 382];

    public static readonly string[] SvVersions = ["scarlet", "violet"];
    public static readonly string[] SvMaps = ["paldea", "kitakami", "blueberry"];
    public static readonly string[] SvProgress = ["beginning", "tera", "3star", "4star", "5star", "6star"];
    public static readonly string[] SvContent = ["standard", "black"];
}

/// <summary>
/// A raid escolhida. Os Pokémon vêm do cofre, no formato do jogo: <see cref="Boss"/> (o chefe da Max Raid montada),
/// <see cref="Offer"/> (o Pokémon que o TradeRock leva no Sword/Shield) e <see cref="Pokemon"/> (o chefe da Tera Raid
/// hospedada, ou o que o TradeRock leva para a do console).
/// </summary>
public sealed record RaidChoice(
    string Kind,
    byte[]? Boss = null,
    byte[]? Offer = null,
    byte[]? Pokemon = null,
    string? Event = null,
    string? EventBoss = null,
    bool Gigantamax = false,
    int Move = 1, // Max Raid em que o TradeRock entra: 1 a 4, ou 0 = a mini IA escolhe (D31)
    IReadOnlyList<int>? TargetTypes = null,   // tipos do chefe informado (ordem do jogo), para a mini IA
    IReadOnlyList<int>? AttackerTypes = null, // tipos do Pokémon que o TradeRock leva
    int? Legendary = null,
    string? Code = null,
    string? Version = null,
    string? Map = null,
    string? Progress = null,
    string? Content = null,
    string? Seed = null,
    int? Group = null,
    int Players = 1);

/// <summary>
/// O presente escolhido: um pronto do pokeldn (<see cref="Key"/> do catalog/gifts.json), um evento oficial do
/// Sword/Shield ou um arquivo (.wc3, .wc8 ou .pokegift) que a pessoa abriu.
/// </summary>
public sealed record GiftChoice(string Mode, string? Key = null, byte[]? Data = null, string? Extension = null);

public sealed record ActivityRequest(TradeGame Game, ActivityKind Kind, TrainerProfile Trainer, GiftChoice? Gift = null,
    RaidChoice? Raid = null, CircleChoice? Circle = null)
{
    public SwitchGame GameInfo => GameCatalog.Of(Game);
}

public enum ActivityEventKind
{
    Started,
    /// <summary>Sala aberta (só com o ESP32: o "[link]" do pokeldn).</summary>
    Waiting,
    Joined,
    Left,
    /// <summary>Presente do FRLG entregue ("[done] delivery N complete").</summary>
    Delivered,
    /// <summary>Presente do Sword/Shield no ar; o console só ouve o anúncio e não confirma.</summary>
    Advertising,
    /// <summary>Raid: a sala abriu (ou o TradeRock entrou na do console).</summary>
    Lobby,
    /// <summary>Raid: a batalha começou.</summary>
    Battle,
    /// <summary>Raid: a batalha acabou (ou o TradeRock deixou o Pokémon lutando e saiu).</summary>
    Over,
    Error,
    Finished,
}

public sealed record ActivityEvent(ActivityEventKind Kind, DateTimeOffset At, string? Message = null)
{
    public string? ErrorCode { get; init; }
}

public sealed record ActivityResult(bool Ok, string? Code = null, string? Message = null)
{
    public static ActivityResult Done { get; } = new(true);
    public static ActivityResult Cancelled { get; } = new(false, "cancelled");
    public static ActivityResult Fail(string code, string? message = null) => new(false, code, message);
}

/// <summary>Motor que também roda as outras atividades (os motores do pokeldn: ESP32 e placa Wi-Fi).</summary>
public interface IActivityEngine
{
    bool SupportsActivity(GameFamily family, ActivityKind kind);

    Task<ActivityResult> RunActivityAsync(ActivityRequest request, Func<ActivityEvent, ValueTask> emit, CancellationToken ct);
}

public sealed record GiftEntry(string Key, string Group, string Label, string Summary, string When = "");

public sealed record GameGifts(IReadOnlyList<GiftEntry> Presets, IReadOnlyList<GiftEntry> Events);

/// <summary>
/// Os presentes que o pokeldn sabe mandar (catalog/gifts.json, gerado por scripts/pokeldn/gift_catalog.py e
/// conferido pelo scripts/test-pokeldn.sh). Só o FRLG e o Sword/Shield têm Mystery Gift local.
/// </summary>
public sealed class GiftCatalog
{
    public static GiftCatalog Embedded { get; } = Load();

    public IReadOnlyDictionary<GameFamily, GameGifts> Games { get; private init; } = new Dictionary<GameFamily, GameGifts>();

    public static readonly IReadOnlyDictionary<GameFamily, string[]> Extensions = new Dictionary<GameFamily, string[]>
    {
        [GameFamily.Frlg] = ["wc3", "pokegift"],
        [GameFamily.Swsh] = ["wc8", "pokegift"],
    };

    /// <summary>Tamanho máximo de um arquivo de presente (gifts.MAX_FILE_SIZE do pokeldn).</summary>
    public const int MaxFileSize = 256 << 10;

    // Tamanhos que o pokeldn aceita: .wc3 (frlg/gift/file.py WC3_SIZE e WC3_JAPANESE_SIZE) e .wc8 (720 bytes).
    private static readonly int[] Wc3Sizes = [0x58C, 0x4E4];
    private const int Wc8Size = 0x2D0;

    public bool Has(GameFamily family, GiftChoice gift) => gift.Mode switch
    {
        "preset" => Games.TryGetValue(family, out var g) && g.Presets.Any(p => p.Key == gift.Key),
        "event" => Games.TryGetValue(family, out var g) && g.Events.Any(e => e.Key == gift.Key),
        "file" => FileProblem(family, gift.Extension, gift.Data) is null,
        _ => false,
    };

    /// <summary>Por que o arquivo não serve, ou null.</summary>
    public static string? FileProblem(GameFamily family, string? extension, byte[]? data)
    {
        if (!Extensions.TryGetValue(family, out var allowed) || extension is null || !allowed.Contains(extension))
            return "gift_file_wrong_type";
        if (data is null || data.Length == 0 || data.Length > MaxFileSize)
            return "gift_file_invalid";
        return extension switch
        {
            "wc3" => Wc3Sizes.Contains(data.Length) ? null : "gift_file_invalid",
            "wc8" => data.Length == Wc8Size ? null : "gift_file_invalid",
            _ => PokegiftGame(data) is not { } game ? "gift_file_invalid"
                : game != GameCatalog.Get(family).Key ? "gift_file_other_game" : null,
        };
    }

    /// <summary>O jogo de um .pokegift (o campo "game" do JSON), ou null se não for um.</summary>
    private static string? PokegiftGame(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.String ? game.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GiftCatalog Load()
    {
        using var stream = typeof(GiftCatalog).Assembly.GetManifestResourceStream("catalog/gifts.json")
            ?? throw new InvalidOperationException("catalog/gifts.json não está embutido.");
        using var doc = JsonDocument.Parse(stream);
        return new GiftCatalog
        {
            Games = doc.RootElement.EnumerateObject().ToDictionary(g => GameCatalog.ByKey(g.Name)!.Family,
                g => new GameGifts(Entries(g.Value, "presets"), Entries(g.Value, "events"))),
        };
    }

    private static List<GiftEntry> Entries(JsonElement game, string name) =>
        game.TryGetProperty(name, out var list)
            ? [.. list.EnumerateArray().Select(e => new GiftEntry(Text(e, "key"), Text(e, "group"), Text(e, "label"), Text(e, "summary"), Text(e, "when")))]
            : [];

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}

/// <summary>
/// As raids de evento embutidas (catalog/raids.json, gerado por scripts/pokeldn/raid_catalog.py a partir de
/// assets/raid-events; D26). O app só confere as escolhas; a tela recebe o arquivo inteiro.
/// </summary>
public sealed class RaidCatalog
{
    public static RaidCatalog Embedded { get; } = Load();

    /// <summary>O arquivo como está (a tela lê direto).</summary>
    public byte[] Json { get; private init; } = [];

    private Dictionary<string, HashSet<string>> SwshBosses { get; init; } = [];
    private Dictionary<string, Dictionary<string, Dictionary<int, HashSet<string>>>> SvGroups { get; init; } = [];

    public bool HasSwshBoss(string eventKey, string boss) => SwshBosses.TryGetValue(eventKey, out var b) && b.Contains(boss);

    /// <summary>Evento do Scarlet/Violet com essa versão, grupo (null = qualquer) e etapa da história.</summary>
    public bool HasSv(string eventKey, string version, int? group, string progress) =>
        SvGroups.TryGetValue(eventKey, out var versions) && versions.TryGetValue(version, out var groups)
        && (group is { } g ? groups.TryGetValue(g, out var p) && p.Contains(progress) : groups.Values.Any(p => p.Contains(progress)));

    private static RaidCatalog Load()
    {
        using var stream = typeof(RaidCatalog).Assembly.GetManifestResourceStream("catalog/raids.json")
            ?? throw new InvalidOperationException("catalog/raids.json não está embutido.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var json = buffer.ToArray();
        using var doc = JsonDocument.Parse(json);
        var swsh = new Dictionary<string, HashSet<string>>();
        foreach (var e in doc.RootElement.GetProperty("swsh").EnumerateArray())
            swsh[e.GetProperty("key").GetString()!] = [.. e.GetProperty("bosses").EnumerateArray().Select(b => b[0].GetString()!)];
        var sv = new Dictionary<string, Dictionary<string, Dictionary<int, HashSet<string>>>>();
        foreach (var e in doc.RootElement.GetProperty("sv").EnumerateArray())
            sv[e.GetProperty("key").GetString()!] = e.GetProperty("versions").EnumerateObject().ToDictionary(
                v => v.Name,
                v => v.Value.EnumerateArray().ToDictionary(g => g.GetProperty("group").GetInt32(),
                    g => g.GetProperty("progress").EnumerateArray().Select(p => p.GetString()!).ToHashSet()));
        return new RaidCatalog { Json = json, SwshBosses = swsh, SvGroups = sv };
    }
}
