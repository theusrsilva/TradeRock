using PKHeX.Core;
using TradeRock.Core.Pokemon;

namespace TradeRock.Core.Builder;

/// <summary>
/// Campos brutos de um .pk3 para o modo avançado (como no PKHeX). Nada é "consertado": o que for
/// definido aqui vai para o arquivo, e o relatório de legalidade diz o que está errado.
/// IVs/EVs na ordem do Showdown (PS, Atq, Def, AtqE, DefE, Vel).
/// </summary>
public sealed record RawPokemon
{
    public ushort Species { get; init; }
    public string? Nickname { get; init; }
    public byte Level { get; init; } = 5;
    public uint Pid { get; init; }
    public bool AbilityBit { get; init; }
    public int[] Ivs { get; init; } = [0, 0, 0, 0, 0, 0];
    public int[] Evs { get; init; } = [0, 0, 0, 0, 0, 0];
    public ushort[] Moves { get; init; } = [0, 0, 0, 0];
    public int[] PpUps { get; init; } = [0, 0, 0, 0];
    public ushort HeldItem { get; init; }
    public int Ball { get; init; } = (int)PKHeX.Core.Ball.Poke;
    public string OtName { get; init; } = "";
    public byte OtGender { get; init; }
    public ushort Tid { get; init; }
    public ushort Sid { get; init; }
    public string Version { get; init; } = nameof(GameVersion.FR);
    public int Language { get; init; } = (int)LanguageID.English;
    public ushort MetLocation { get; init; }
    public byte MetLevel { get; init; }
    public bool Fateful { get; init; }
    public byte Friendship { get; init; } = 70;

    public static RawPokemon FromPk(PK3 pk)
    {
        var speciesName = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, 3);
        return new RawPokemon
        {
            Species = pk.Species,
            Nickname = pk.Nickname == speciesName ? null : pk.Nickname,
            Level = pk.CurrentLevel,
            Pid = pk.PID,
            AbilityBit = pk.AbilityBit,
            Ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE],
            Evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE],
            Moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4],
            PpUps = [pk.Move1_PPUps, pk.Move2_PPUps, pk.Move3_PPUps, pk.Move4_PPUps],
            HeldItem = (ushort)pk.HeldItem,
            Ball = pk.Ball,
            OtName = pk.OriginalTrainerName,
            OtGender = pk.OriginalTrainerGender,
            Tid = pk.TID16,
            Sid = pk.SID16,
            Version = pk.Version.ToString(),
            Language = pk.Language,
            MetLocation = pk.MetLocation,
            MetLevel = pk.MetLevel,
            Fateful = pk.FatefulEncounter,
            Friendship = pk.OriginalTrainerFriendship,
        };
    }
}

/// <summary>O que sai do PID (na Gen 3 não se escolhe: calcula-se).</summary>
public sealed record DerivedFromPid(int Nature, string? Gender, bool Shiny, string Ability, string PidIvMethod);

public sealed record AdvancedResult(
    IReadOnlyList<string> Errors,
    byte[]? Data,
    Pk3Summary? Summary,
    LegalityReport? Legality,
    DerivedFromPid? Derived,
    RawPokemon? Raw);

public static class AdvancedEditor
{
    public static readonly string[] Versions = [nameof(GameVersion.FR), nameof(GameVersion.LG), nameof(GameVersion.E),
        nameof(GameVersion.R), nameof(GameVersion.S), nameof(GameVersion.CXD)];

    public static IReadOnlyList<string> Validate(RawPokemon raw)
    {
        var errors = new List<string>();
        if (raw.Species is < 1 or > BuilderData.MaxSpecies) errors.Add("species_invalid");
        if (raw.Level is < 1 or > 100) errors.Add("level_invalid");
        if (raw.Ivs.Length != 6 || raw.Ivs.Any(v => v is < 0 or > 31)) errors.Add("ivs_invalid");
        if (raw.Evs.Length != 6 || raw.Evs.Any(v => v is < 0 or > 255)) errors.Add("evs_invalid");
        if (raw.Moves.Length != 4 || raw.Moves.Any(m => m > BuilderData.MaxMove)) errors.Add("moves_invalid");
        if (raw.PpUps.Length != 4 || raw.PpUps.Any(v => v is < 0 or > 3)) errors.Add("ppups_invalid");
        if (raw.Ball is < 1 or > 12) errors.Add("ball_invalid");
        if (!Versions.Contains(raw.Version)) errors.Add("version_invalid");
        if (raw.MetLevel > 100) errors.Add("met_level_invalid");
        if (raw.OtName.Length is < 1 or > 7) errors.Add("ot_invalid");
        if (raw.Nickname is { Length: > 10 }) errors.Add("nickname_too_long");
        return errors;
    }

    public static PK3 ToPk(RawPokemon raw)
    {
        var pk = new PK3
        {
            Species = raw.Species,
            Language = raw.Language,
            Version = Enum.Parse<GameVersion>(raw.Version),
            PID = raw.Pid,
            AbilityBit = raw.AbilityBit,
            IV_HP = raw.Ivs[0], IV_ATK = raw.Ivs[1], IV_DEF = raw.Ivs[2],
            IV_SPA = raw.Ivs[3], IV_SPD = raw.Ivs[4], IV_SPE = raw.Ivs[5],
            EV_HP = raw.Evs[0], EV_ATK = raw.Evs[1], EV_DEF = raw.Evs[2],
            EV_SPA = raw.Evs[3], EV_SPD = raw.Evs[4], EV_SPE = raw.Evs[5],
            HeldItem = raw.HeldItem,
            Ball = (byte)raw.Ball,
            OriginalTrainerName = raw.OtName,
            OriginalTrainerGender = raw.OtGender,
            TID16 = raw.Tid,
            SID16 = raw.Sid,
            MetLocation = raw.MetLocation,
            MetLevel = raw.MetLevel,
            FatefulEncounter = raw.Fateful,
            OriginalTrainerFriendship = raw.Friendship,
        };
        pk.SetMoves(raw.Moves);
        pk.Move1_PPUps = raw.PpUps[0];
        pk.Move2_PPUps = raw.PpUps[1];
        pk.Move3_PPUps = raw.PpUps[2];
        pk.Move4_PPUps = raw.PpUps[3];
        pk.HealPP();
        pk.CurrentLevel = raw.Level;

        var speciesName = SpeciesName.GetSpeciesNameGeneration(raw.Species, raw.Language, 3);
        var nickname = string.IsNullOrWhiteSpace(raw.Nickname) ? speciesName : raw.Nickname.Trim();
        pk.IsNicknamed = nickname != speciesName;
        pk.Nickname = nickname;

        pk.ResetPartyStats();
        pk.RefreshChecksum();
        return pk;
    }

    public static AdvancedResult Build(RawPokemon raw, string language)
    {
        var errors = Validate(raw);
        if (errors.Count > 0)
            return new AdvancedResult(errors, null, null, null, null, null);

        var pk = ToPk(raw);
        var legality = LegalityReporter.Analyze(pk, language);
        var data = new byte[Pk3Reader.PartySize];
        pk.WriteDecryptedDataParty(data);

        var pi = PersonalTable.E[pk.Species];
        var ability = BuilderData.Strings.abilitylist[pi.GetAbility(pk.AbilityBit)];
        var method = MethodFinder.Analyze(pk).Type.ToString();
        var derived = new DerivedFromPid((int)pk.Nature, pk.Gender switch { 0 => "M", 1 => "F", _ => null },
            pk.IsShiny, ability, method);

        return new AdvancedResult([], data, Pk3Reader.Read(data).Summary, legality, derived, RawPokemon.FromPk(pk));
    }
}
