using PKHeX.Core;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Builder;

public sealed record Option(int Value, string Text);

public sealed record SpeciesDetails(
    ushort Species,
    string Name,
    IReadOnlyList<Option> Abilities, // Value = slot (0 ou 1; 2 = habilidade oculta, da Gen 5 em diante)
    string GenderRatio, // "genderless" | "male_only" | "female_only" | "mixed"
    bool InKantoDex,
    IReadOnlyList<Option>? Forms = null); // formas que existem no jogo (Value = número da forma)

/// <summary>Listas do formulário do construtor para um jogo (Entrega 14).</summary>
public sealed record GameBuilderData(
    IReadOnlyList<Option> Species,
    IReadOnlyList<Option> Natures,
    IReadOnlyList<Option> Moves,
    IReadOnlyList<Option> Items,
    IReadOnlyList<Option> Balls,
    IReadOnlyList<Option> Languages,
    int MaxEv,
    int NicknameLength);

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

    public static GameVersion ToVersion(TradeGame game) => GameCatalog.ToVersion(game);

    private static readonly Dictionary<GameFamily, GameBuilderData> PerGame = [];

    /// <summary>
    /// Listas de um jogo: espécies que existem nele (alguma forma), golpes que ele usa (sem os "dummied"),
    /// itens e bolas do save vazio do jogo. O FireRed/LeafGreen mantém as listas da Gen 3 acima.
    /// </summary>
    public static GameBuilderData For(SwitchGame game)
    {
        lock (PerGame)
        {
            if (PerGame.TryGetValue(game.Family, out var cached))
                return cached;
            GameBuilderData data;
            if (game.Family == GameFamily.Frlg)
            {
                data = new GameBuilderData(Species, Natures, Moves, Items, Balls, Languages, 255, 10);
            }
            else
            {
                var sav = BlankSaveFile.Get(GameCatalog.ToVersion(game.Versions[0]), "TRADEROCK", LanguageID.English);
                var source = new FilteredGameDataSource(sav, GameInfo.Sources, false);
                var table = game.PersonalTable;
                var species = Enumerable.Range(1, table.MaxSpeciesID)
                    .Where(i => Enumerable.Range(0, Math.Max(1, (int)table[(ushort)i].FormCount)).Any(f => table.IsPresentInGame((ushort)i, (byte)f)))
                    .Select(i => new Option(i, Strings.specieslist[i])).ToList();
                var dummied = MoveInfo.GetDummiedMovesHashSet(game.Context);
                var moves = new List<Option>();
                for (ushort m = 1; m <= game.MaxMoveId; m++)
                {
                    if (!MoveInfo.IsDummiedMove(dummied, m))
                        moves.Add(new Option(m, Strings.movelist[m]));
                }
                moves = moves.OrderBy(o => o.Text).ToList();
                var blank = game.Blank();
                data = new GameBuilderData(
                    species, Natures, moves,
                    source.Items.Where(i => i.Value > 0).Select(i => new Option(i.Value, i.Text)).ToList(),
                    source.Balls.Where(b => b.Value > 0).Select(b => new Option(b.Value, b.Text)).ToList(),
                    [.. Languages, new((int)LanguageID.Korean, "한국어"), new((int)LanguageID.ChineseS, "简体中文"),
                     new((int)LanguageID.ChineseT, "繁體中文")],
                    blank.MaxEV, blank.MaxStringLengthNickname);
            }
            PerGame[game.Family] = data;
            return data;
        }
    }

    /// <summary>Espécie, habilidades e formas num jogo. No FRLG é o mesmo que <see cref="Details(ushort)"/>.</summary>
    public static SpeciesDetails Details(ushort species, SwitchGame game, byte form = 0)
    {
        if (game.Family == GameFamily.Frlg)
            return Details(species);
        var table = game.PersonalTable;
        var pi = table.GetFormEntry(species, form);
        var abilities = new List<Option>();
        for (var slot = 0; slot < pi.AbilityCount; slot++)
        {
            var ability = pi.GetAbilityAtIndex(slot);
            if (ability > 0 && abilities.All(a => a.Text != Strings.abilitylist[ability]))
                abilities.Add(new Option(slot, Strings.abilitylist[ability]));
        }
        var names = FormConverter.GetFormList(species, Strings.types, Strings.forms, game.Context);
        var forms = Enumerable.Range(0, Math.Max(1, (int)table[species].FormCount))
            .Where(f => table.IsPresentInGame(species, (byte)f))
            .Select(f => new Option(f, f < names.Length && names[f].Length > 0 ? names[f] : f == 0 ? "" : $"#{f}"))
            .ToList();
        var ratio = pi.Genderless ? "genderless" : pi.OnlyMale ? "male_only" : pi.OnlyFemale ? "female_only" : "mixed";
        return new SpeciesDetails(species, Strings.specieslist[species], abilities, ratio, false, forms);
    }

    /// <summary>Encontros possíveis da espécie nas versões de um jogo novo (no FRLG, ver a sobrecarga da Gen 3).</summary>
    public static IReadOnlyList<EncounterOption> Encounters(ushort species, TradeGame target, byte form)
    {
        var game = GameCatalog.Of(target);
        if (game.Family == GameFamily.Frlg)
            return Encounters(species, target);
        AutoModEnvironment.EnsureInitialized();
        var pk = game.Blank();
        pk.Species = species;
        pk.Form = form;
        pk.Language = (int)LanguageID.English;
        pk.SetGender(pk.GetSaneGender());
        // A versão escolhida primeiro, depois a outra do par.
        var versions = game.Versions.OrderBy(v => v == target ? 0 : 1).Select(GameCatalog.ToVersion).ToArray();
        var result = new List<EncounterOption>();
        var seen = new HashSet<string>();
        foreach (var enc in EncounterMovesetGenerator.GenerateEncounters(pk, ReadOnlyMemory<ushort>.Empty, versions))
        {
            var option = ToOption(enc, game.Format);
            if (seen.Add(option.Key))
                result.Add(option);
        }
        return result.OrderBy(e => e.Type is "event" ? 1 : 0).ToList();
    }

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

    public static EncounterOption ToOption(IEncounterable enc) => ToOption(enc, 3);

    /// <param name="format">Formato do PKHeX do jogo de destino (3 = Gen 3), para o nome do local.</param>
    public static EncounterOption ToOption(IEncounterable enc, byte format)
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
        var locationName = format == 3
            ? Strings.GetLocationName(false, location, 3, 3, enc.Version)
            : Strings.GetLocationName(false, location, format, enc.Generation, enc.Version);
        var fateful = enc is IFatefulEncounterReadOnly { FatefulEncounter: true };
        // Na Gen 3, o local 255 é "encontro fatídico": presentes ali são distribuições de evento (ex.: WISHMKR).
        if (format == 3 && type == "gift" && location == EventLocation)
            type = "event";
        var key = $"{enc.Version}|{location}|{enc.LevelMin}|{enc.Species}|{type}";
        return new EncounterOption(key, type, enc.Version.ToString(), enc.Species, Strings.specieslist[enc.Species],
            location, locationName, enc.LevelMin, enc.LevelMax, fateful);
    }
}
