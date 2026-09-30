using PKHeX.Core;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Builder;

public sealed record Option(int Value, string Text);

public sealed record SpeciesDetails(
    ushort Species,
    string Name,
    IReadOnlyList<Option> Abilities, // Value = slot (0 ou 1)
    string GenderRatio, // "genderless" | "male_only" | "female_only" | "mixed"
    bool InKantoDex);

/// <summary>Um encontro possível (origem) para a espécie, vindo da base do PKHeX.</summary>
public sealed record EncounterOption(
    string Key,
    string Type, // wild | static | gift | event | trade | egg | shadow | other
    string Version,
    ushort Species,
    string SpeciesName,
    ushort Location,
    string LocationName,
    byte LevelMin,
    byte LevelMax,
    bool Fateful);

/// <summary>Listas para o formulário do construtor (nomes em inglês, decisão do projeto).</summary>
public static class BuilderData
{
    public const ushort MaxSpecies = 386;
    public const ushort MaxMove = 354; // último golpe da Gen 3 (Psycho Boost)
    public const ushort KantoEnd = 151; // KANTO_SPECIES_END na pokefirered
    public const ushort EventLocation = 255; // "fateful encounter" nos jogos da Gen 3

    public static GameStrings Strings { get; } = GameInfo.GetStrings("en");

    private static readonly Lazy<FilteredGameDataSource> Sources = new(() =>
        new FilteredGameDataSource(BlankSaveFile.Get(GameVersion.FR, "TRADEROCK"), GameInfo.Sources));

    public static IReadOnlyList<Option> Species { get; } =
        Enumerable.Range(1, MaxSpecies).Select(i => new Option(i, Strings.specieslist[i])).ToList();

    public static IReadOnlyList<Option> Natures { get; } =
        Enumerable.Range(0, 25).Select(i => new Option(i, Strings.natures[i])).ToList();

    public static IReadOnlyList<Option> Moves { get; } =
        Enumerable.Range(1, MaxMove).Select(i => new Option(i, Strings.movelist[i])).OrderBy(o => o.Text).ToList();

    /// <summary>Itens seguráveis na Gen 3 (IDs internos da Gen 3, como ficam no .pk3).</summary>
    public static IReadOnlyList<Option> Items => Sources.Value.Items
        .Where(i => i.Value > 0).Select(i => new Option(i.Value, i.Text)).ToList();

    public static IReadOnlyList<Option> Balls => Sources.Value.Balls
        .Where(b => b.Value is > 0 and <= 12).Select(b => new Option(b.Value, b.Text)).ToList();

    public static IReadOnlyList<Option> Languages { get; } =
    [
        new((int)LanguageID.English, "English"),
        new((int)LanguageID.Japanese, "日本語"),
        new((int)LanguageID.French, "Français"),
        new((int)LanguageID.Italian, "Italiano"),
        new((int)LanguageID.German, "Deutsch"),
        new((int)LanguageID.Spanish, "Español"),
    ];

    public static GameVersion ToVersion(TradeGame game) => game == TradeGame.LeafGreen ? GameVersion.LG : GameVersion.FR;

    public static SpeciesDetails Details(ushort species)
    {
        var pi = PersonalTable.E[species];
        var abilities = new List<Option> { new(0, Strings.abilitylist[pi.Ability1]) };
        if (pi.HasSecondAbility)
            abilities.Add(new Option(1, Strings.abilitylist[pi.Ability2]));

        var ratio = pi.Genderless ? "genderless" : pi.OnlyMale ? "male_only" : pi.OnlyFemale ? "female_only" : "mixed";
        return new SpeciesDetails(species, Strings.specieslist[species], abilities, ratio, species <= KantoEnd);
    }

    /// <summary>Encontros possíveis da espécie nos jogos da Gen 3, na ordem de preferência do jogo alvo.</summary>
    public static IReadOnlyList<EncounterOption> Encounters(ushort species, TradeGame target)
    {
        AutoModEnvironment.EnsureInitialized();
        var pk = new PK3 { Species = species, Language = (int)LanguageID.English };
        pk.SetGender(pk.GetSaneGender());
        var priority = AutoModEnvironment.PriorityFor(ToVersion(target));

        var result = new List<EncounterOption>();
        var seen = new HashSet<string>();
        foreach (var version in priority)
        {
            foreach (var enc in EncounterMovesetGenerator.GenerateEncounters(pk, ReadOnlyMemory<ushort>.Empty, version))
            {
                var option = ToOption(enc);
                if (seen.Add(option.Key))
                    result.Add(option);
            }
        }

        // Ordem de preferência: encontros do jogo escolhido, do outro FRLG, de RSE e de Colosseum/XD;
        // dentro de cada jogo, estático/selvagem/troca/presente, depois ovo; eventos e Shadow por último.
        var targetVersion = ToVersion(target);
        int VersionRank(string version) => version switch
        {
            _ when version == targetVersion.ToString() || version == nameof(GameVersion.FRLG) => 0,
            nameof(GameVersion.FR) or nameof(GameVersion.LG) => 1,
            nameof(GameVersion.E) => 2,
            nameof(GameVersion.R) or nameof(GameVersion.S) or nameof(GameVersion.RS) or nameof(GameVersion.RSE) => 3,
            _ => 4,
        };
        static int TypeRank(string type) => type switch
        {
            "static" or "wild" or "trade" or "gift" => 0,
            "egg" => 1,
            "shadow" => 2,
            _ => 3, // event, other
        };
        return result
            .Select((e, index) => (e, index))
            .OrderBy(x => x.e.Type is "event" ? 1 : 0)
            .ThenBy(x => VersionRank(x.e.Version))
            .ThenBy(x => TypeRank(x.e.Type))
            .ThenBy(x => x.index)
            .Select(x => x.e)
            .ToList();
    }

    public static EncounterOption ToOption(IEncounterable enc)
    {
        var type = enc switch
        {
            MysteryGift => "event",
            _ => enc.GetType().Name switch
            {
                var n when n.Contains("Slot") => "wild",
                var n when n.Contains("Shadow") => "shadow",
                var n when n.Contains("Static") => "static",
                var n when n.Contains("Gift") => "gift",
                var n when n.Contains("Trade") => "trade",
                var n when n.Contains("Egg") => "egg",
                _ => "other",
            },
        };
        var location = enc.Location;
        var locationName = Strings.GetLocationName(false, location, 3, 3, enc.Version);
        var fateful = enc is IFatefulEncounterReadOnly { FatefulEncounter: true };
        // Na Gen 3, o local 255 é "encontro fatídico": presentes ali são distribuições de evento (ex.: WISHMKR).
        if (type == "gift" && location == EventLocation)
            type = "event";
        var key = $"{enc.Version}|{location}|{enc.LevelMin}|{enc.Species}|{type}";
        return new EncounterOption(key, type, enc.Version.ToString(), enc.Species, Strings.specieslist[enc.Species],
            location, locationName, enc.LevelMin, enc.LevelMax, fateful);
    }
}
