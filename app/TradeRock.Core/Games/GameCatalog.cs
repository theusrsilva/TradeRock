using PKHeX.Core;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Games;

/// <summary>Jogo do Switch com troca local, como o TradeRock os agrupa (as duas versões de um par juntas).</summary>
public enum GameFamily
{
    Frlg,
    Lgpe,
    Swsh,
    Bdsp,
    Pla,
    Sv,
    Za,
}

/// <summary>Como o jogo pede o código de troca (D21).</summary>
public enum LinkCodeKind
{
    /// <summary>FireRed/LeafGreen: sem código (Direct Corner).</summary>
    None,

    /// <summary>Oito dígitos (Sword/Shield, BDSP, Arceus, Scarlet/Violet, Legends: Z-A).</summary>
    Digits8,

    /// <summary>Let's Go: três Pokémon escolhidos numa grade.</summary>
    ThreePokemon,
}

/// <summary>Outras atividades pela rede local que o pokeldn faz em cada jogo (Entrega 18).</summary>
public enum GameActivity
{
    Trade,
    MysteryGift,
    MaxRaid,
    DynamaxAdventure,
    TeraRaid,
    UnionCircle,
}

/// <summary>
/// Um jogo: o formato de arquivo do PKHeX, as versões e o que a troca precisa. Comm ids e versões em que o
/// pokeldn estudou cada jogo estão na D21.
/// </summary>
public sealed record SwitchGame(
    GameFamily Family,
    string Key,
    string Name,
    IReadOnlyList<TradeGame> Versions,
    EntityContext Context,
    Type EntityType,
    string Extension,
    LinkCodeKind LinkCode,
    bool LanMode,
    IReadOnlyList<GameActivity> Activities)
{
    /// <summary>Jogos da Gen 7 em diante mostram o ID de treinador com 6 dígitos (TID7) e o SID com 4.</summary>
    public bool SixDigitTrainerId => Family != GameFamily.Frlg;

    /// <summary>Tamanho máximo do nome do treinador no jogo.</summary>
    public int MaxTrainerNameLength => Family == GameFamily.Frlg ? 7 : 12;

    public IPersonalTable PersonalTable => Family switch
    {
        GameFamily.Frlg => PKHeX.Core.PersonalTable.FR,
        GameFamily.Lgpe => PKHeX.Core.PersonalTable.GG,
        GameFamily.Swsh => PKHeX.Core.PersonalTable.SWSH,
        GameFamily.Bdsp => PKHeX.Core.PersonalTable.BDSP,
        GameFamily.Pla => PKHeX.Core.PersonalTable.LA,
        GameFamily.Sv => PKHeX.Core.PersonalTable.SV,
        GameFamily.Za => PKHeX.Core.PersonalTable.ZA,
        _ => throw new ArgumentOutOfRangeException(nameof(Family)),
    };

    /// <summary>Pokémon vazio do formato do jogo (um novo a cada chamada).</summary>
    public PKM Blank() => (PKM)Activator.CreateInstance(EntityType)!;

    /// <summary>Formato do PKHeX (3, 7, 8 ou 9), que o AutoMod usa como "geração" do pedido.</summary>
    public byte Format => _format ??= Blank().Format;
    private byte? _format;

    public ushort MaxMoveId => _maxMove ??= Blank().MaxMoveID;
    private ushort? _maxMove;

    /// <summary>A espécie (e a forma) existe neste jogo.</summary>
    public bool HasSpecies(ushort species, byte form = 0) =>
        species is > 0 && species <= PersonalTable.MaxSpeciesID && PersonalTable.IsPresentInGame(species, form);
}

public static class GameCatalog
{
    public static IReadOnlyList<SwitchGame> All { get; } =
    [
        new(GameFamily.Frlg, "frlg", "FireRed/LeafGreen", [TradeGame.FireRed, TradeGame.LeafGreen],
            EntityContext.Gen3, typeof(PK3), "pk3", LinkCodeKind.None, false,
            [GameActivity.Trade, GameActivity.MysteryGift]),
        new(GameFamily.Lgpe, "lgpe", "Let's Go, Pikachu!/Eevee!", [TradeGame.LetsGoPikachu, TradeGame.LetsGoEevee],
            EntityContext.Gen7b, typeof(PB7), "pb7", LinkCodeKind.ThreePokemon, false,
            [GameActivity.Trade]),
        new(GameFamily.Swsh, "swsh", "Sword/Shield", [TradeGame.Sword, TradeGame.Shield],
            EntityContext.Gen8, typeof(PK8), "pk8", LinkCodeKind.Digits8, true,
            [GameActivity.Trade, GameActivity.MysteryGift, GameActivity.MaxRaid, GameActivity.DynamaxAdventure]),
        new(GameFamily.Bdsp, "bdsp", "Brilliant Diamond/Shining Pearl", [TradeGame.BrilliantDiamond, TradeGame.ShiningPearl],
            EntityContext.Gen8b, typeof(PB8), "pb8", LinkCodeKind.Digits8, false,
            [GameActivity.Trade]),
        new(GameFamily.Pla, "pla", "Legends: Arceus", [TradeGame.LegendsArceus],
            EntityContext.Gen8a, typeof(PA8), "pa8", LinkCodeKind.Digits8, false,
            [GameActivity.Trade]),
        new(GameFamily.Sv, "sv", "Scarlet/Violet", [TradeGame.Scarlet, TradeGame.Violet],
            EntityContext.Gen9, typeof(PK9), "pk9", LinkCodeKind.Digits8, false,
            [GameActivity.Trade, GameActivity.TeraRaid, GameActivity.UnionCircle]),
        new(GameFamily.Za, "za", "Legends: Z-A", [TradeGame.LegendsZA],
            EntityContext.Gen9a, typeof(PA9), "pa9", LinkCodeKind.Digits8, false,
            [GameActivity.Trade]),
    ];

    public static SwitchGame Get(GameFamily family) => All[(int)family];

    /// <summary>
    /// Local communication ids das salas de troca de cada jogo (D21; o FRLG também com o id do frlg-ldn-trade).
    /// O Shield usa o id do Sword, o Violet o do Scarlet e o Eevee o do Pikachu.
    /// </summary>
    public static IReadOnlyDictionary<ulong, GameFamily> CommunicationIds { get; } = new Dictionary<ulong, GameFamily>
    {
        [0x01006fa0233f8000] = GameFamily.Frlg,
        [0x0100610011000000] = GameFamily.Frlg,
        [0x010003F003A34000] = GameFamily.Lgpe,
        [0x0100ABF008968000] = GameFamily.Swsh,
        [0x0100000011D90000] = GameFamily.Bdsp,
        [0x01001f5010dfa000] = GameFamily.Pla,
        [0x0100a3d008c5c000] = GameFamily.Sv,
        [0x0100f43008c44000] = GameFamily.Za,
    };

    public static SwitchGame Of(TradeGame version) => All.First(g => g.Versions.Contains(version));

    public static SwitchGame? ByKey(string key) => All.FirstOrDefault(g => g.Key == key);

    public static SwitchGame? ByExtension(string extension) =>
        All.FirstOrDefault(g => string.Equals(g.Extension, extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase));

    public static SwitchGame? ByContext(EntityContext context) => All.FirstOrDefault(g => g.Context == context);

    /// <summary>Versão do PKHeX de cada versão do TradeRock.</summary>
    public static GameVersion ToVersion(TradeGame game) => game switch
    {
        TradeGame.FireRed => GameVersion.FR,
        TradeGame.LeafGreen => GameVersion.LG,
        TradeGame.LetsGoPikachu => GameVersion.GP,
        TradeGame.LetsGoEevee => GameVersion.GE,
        TradeGame.Sword => GameVersion.SW,
        TradeGame.Shield => GameVersion.SH,
        TradeGame.BrilliantDiamond => GameVersion.BD,
        TradeGame.ShiningPearl => GameVersion.SP,
        TradeGame.LegendsArceus => GameVersion.PLA,
        TradeGame.Scarlet => GameVersion.SL,
        TradeGame.Violet => GameVersion.VL,
        TradeGame.LegendsZA => GameVersion.ZA,
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };
}

/// <summary>
/// IDs de treinador. O arquivo guarda TID16 e SID16 (32 bits juntos); da Gen 7 em diante o jogo mostra o
/// mesmo número de 32 bits como TID de 6 dígitos (ID32 % 1.000.000) e SID de 4 (ID32 / 1.000.000).
/// </summary>
public static class TrainerIds
{
    public static (uint Tid, uint Sid) Display(SwitchGame game, ushort tid16, ushort sid16)
    {
        if (!game.SixDigitTrainerId)
            return (tid16, sid16);
        var id32 = ((uint)sid16 << 16) | tid16;
        return (id32 % 1_000_000, id32 / 1_000_000);
    }

    /// <summary>TID16/SID16 a partir do que o jogo mostra. Null quando o número não cabe (SID7 acima de 4294).</summary>
    public static (ushort Tid16, ushort Sid16)? FromDisplay(SwitchGame game, uint tid, uint sid)
    {
        if (!game.SixDigitTrainerId)
            return tid <= ushort.MaxValue && sid <= ushort.MaxValue ? ((ushort)tid, (ushort)sid) : null;
        if (tid >= 1_000_000)
            return null;
        var id32 = (ulong)sid * 1_000_000 + tid;
        if (id32 > uint.MaxValue)
            return null;
        return ((ushort)(id32 & 0xFFFF), (ushort)(id32 >> 16));
    }
}
