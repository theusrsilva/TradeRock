using System.Text;
using PKHeX.Core;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Builder;

/// <summary>Treinador original do Pokémon criado.</summary>
public sealed record TrainerSpec(string Name, ushort Tid, ushort Sid, byte Gender = 0);

/// <summary>
/// O que a pessoa pediu no modo automático. Campos nulos = "tanto faz" (o gerador escolhe).
/// Ordem dos IVs/EVs: PS, Ataque, Defesa, Ataque Especial, Defesa Especial, Velocidade (a do Showdown).
/// </summary>
public sealed record PokemonSpec
{
    public ushort Species { get; init; }
    public string? Nickname { get; init; }
    public byte Level { get; init; } = 50;
    public int? Nature { get; init; }
    public int? AbilitySlot { get; init; }
    public string? Gender { get; init; } // "M" | "F" | null
    public bool Shiny { get; init; }
    public int[]? Ivs { get; init; }
    public int[] Evs { get; init; } = [0, 0, 0, 0, 0, 0];
    public ushort[] Moves { get; init; } = [];
    public ushort HeldItem { get; init; }
    public int? Ball { get; init; }
    public int Language { get; init; } = (int)LanguageID.English;

    /// <summary>Jogo do Switch para onde o Pokémon vai (define a preferência de origem).</summary>
    public TradeGame Game { get; init; } = TradeGame.FireRed;

    /// <summary>Encontro escolhido (<see cref="EncounterOption.Key"/>); null = automático.</summary>
    public string? EncounterKey { get; init; }

    public required TrainerSpec Trainer { get; init; }

    public static readonly string[] StatLabels = ["HP", "Atk", "Def", "SpA", "SpD", "Spe"];

    /// <summary>Validações que não dependem do PKHeX (códigos estáveis).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Species is < 1 or > BuilderData.MaxSpecies)
            errors.Add("species_invalid");
        if (Level is < 1 or > 100)
            errors.Add("level_invalid");
        if (Nature is < 0 or > 24)
            errors.Add("nature_invalid");
        if (AbilitySlot is < 0 or > 1)
            errors.Add("ability_invalid");
        if (Ivs is not null && (Ivs.Length != 6 || Ivs.Any(v => v is < 0 or > 31)))
            errors.Add("ivs_invalid");
        if (Evs.Length != 6 || Evs.Any(v => v is < 0 or > 255))
            errors.Add("evs_invalid");
        if (Evs.Sum() > 510)
            errors.Add("evs_total");
        if (Moves.Length > 4 || Moves.Any(m => m > BuilderData.MaxMove))
            errors.Add("moves_invalid");
        if (Moves.Where(m => m != 0).GroupBy(m => m).Any(g => g.Count() > 1))
            errors.Add("moves_repeated");
        if (Nickname is { Length: > 10 })
            errors.Add("nickname_too_long");
        if (Nickname is not null && Nickname.Any(c => c is '(' or ')' or '@' or '\n' or '\r'))
            errors.Add("nickname_invalid_chars");
        if (Trainer.Name.Length is < 1 or > 7)
            errors.Add("ot_invalid");
        return errors;
    }

    /// <summary>
    /// Texto no formato do Showdown + linhas extras do AutoMod (RegenTemplate): Ball, Language, OT, TID,
    /// SID, OTGender e filtros de encontro "~=Prop=Valor". O item entra depois da geração (não afeta
    /// PID/IV e evita a conversão de nomes de itens entre gerações).
    /// </summary>
    public string ToRegenText(EncounterOption? encounter)
    {
        var s = BuilderData.Strings;
        var sb = new StringBuilder();

        // Apelido pelo AutoMod: ele grava os "bytes residuais" do nome como o jogo faria (senão o PKHeX recusa).
        if (!string.IsNullOrWhiteSpace(Nickname))
            sb.Append($"{Nickname.Trim()} ({s.specieslist[Species]})");
        else
            sb.Append(s.specieslist[Species]);
        if (Gender is "M" or "F")
            sb.Append($" ({Gender})");
        sb.AppendLine();

        if (AbilitySlot is { } slot)
        {
            var pi = PersonalTable.E[Species];
            sb.AppendLine($"Ability: {s.abilitylist[pi.GetAbility(slot == 1)]}");
        }
        sb.AppendLine($"Level: {Level}");
        if (Shiny)
            sb.AppendLine("Shiny: Yes");

        var evs = Evs.Select((v, i) => (v, i)).Where(x => x.v > 0).Select(x => $"{x.v} {StatLabels[x.i]}").ToList();
        if (evs.Count > 0)
            sb.AppendLine("EVs: " + string.Join(" / ", evs));

        if (Nature is { } nature)
            sb.AppendLine($"{s.natures[nature]} Nature");

        if (Ivs is not null)
            sb.AppendLine("IVs: " + string.Join(" / ", Ivs.Select((v, i) => $"{v} {StatLabels[i]}")));

        foreach (var move in Moves.Where(m => m != 0))
            sb.AppendLine($"- {s.movelist[move]}");

        if (Ball is { } ball and > 0)
            sb.AppendLine($"Ball: {(Ball)ball} Ball");
        sb.AppendLine($"Language: {(LanguageID)Language}");
        sb.AppendLine($"OT: {Trainer.Name}");
        sb.AppendLine($"TID: {Trainer.Tid}");
        sb.AppendLine($"SID: {Trainer.Sid}");
        sb.AppendLine($"OTGender: {(Trainer.Gender == 1 ? "Female" : "Male")}");

        if (encounter is not null)
        {
            sb.AppendLine($"~=Version={encounter.Version}");
            sb.AppendLine($"~=Location={encounter.Location}");
            sb.AppendLine($"~=LevelMin={encounter.LevelMin}");
            sb.AppendLine($"~=Species={encounter.Species}");
        }
        return sb.ToString();
    }

    /// <summary>Descreve um PK3 existente no mesmo formato (para editar e para comparar).</summary>
    public static PokemonSpec FromPk(PK3 pk, TradeGame game = TradeGame.FireRed)
    {
        var speciesName = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, 3);
        return new PokemonSpec
        {
            Species = pk.Species,
            Nickname = pk.IsEgg || pk.Nickname == speciesName ? null : pk.Nickname,
            Level = pk.CurrentLevel,
            Nature = (int)pk.Nature,
            AbilitySlot = pk.AbilityBit ? 1 : 0,
            Gender = pk.Gender switch { 0 => "M", 1 => "F", _ => null },
            Shiny = pk.IsShiny,
            Ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE],
            Evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE],
            Moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4],
            HeldItem = (ushort)pk.HeldItem,
            Ball = pk.Ball,
            Language = pk.Language,
            Game = game,
            Trainer = new TrainerSpec(pk.OriginalTrainerName, pk.TID16, pk.SID16, pk.OriginalTrainerGender),
        };
    }
}
