using System.Text.Json;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Activities;

/// <summary>Outras atividades do pokeldn além da troca (D26). Por enquanto, o Mystery Gift.</summary>
public enum ActivityKind
{
    Gift,
}

/// <summary>
/// O presente escolhido: um pronto do pokeldn (<see cref="Key"/> do catalog/gifts.json), um evento oficial do
/// Sword/Shield ou um arquivo (.wc3, .wc8 ou .pokegift) que a pessoa abriu.
/// </summary>
public sealed record GiftChoice(string Mode, string? Key = null, byte[]? Data = null, string? Extension = null);

public sealed record ActivityRequest(TradeGame Game, ActivityKind Kind, TrainerProfile Trainer, GiftChoice? Gift = null)
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
