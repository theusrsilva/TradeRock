using System.Text;
using System.Text.Json.Serialization;
using PKHeX.Core;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Builder;

/// <summary>
/// Treinador original do Pokémon criado, com os números que o jogo mostra: TID/SID de 5 dígitos no FRLG,
/// TID de 6 e SID de 4 da Gen 7 em diante (o AutoMod converte, ver <see cref="TrainerIds"/>).
/// </summary>
public sealed record TrainerSpec(string Name, uint Tid, uint Sid, byte Gender = 0);

/// <summary>
/// O que a pessoa pediu no modo automático. Campos nulos = "tanto faz" (o gerador escolhe).
/// Ordem dos IVs/EVs: PS, Ataque, Defesa, Ataque Especial, Defesa Especial, Velocidade (a do Showdown).
/// </summary>
public sealed record PokemonSpec
{
    public ushort Species { get; init; }

    /// <summary>Forma (Alola, Galar, Hisui, Paldea...); 0 = a normal. Só nos jogos novos.</summary>
    public byte Form { get; init; }

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

    [JsonIgnore]
    public SwitchGame GameInfo => GameCatalog.Of(Game);

    /// <summary>Validações que não dependem do PKHeX (códigos estáveis). Limites do jogo escolhido.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Game))
            return ["game_invalid"];
        var game = GameInfo;
        var gen3 = game.Family == GameFamily.Frlg;
        if (gen3 ? Species is < 1 or > BuilderData.MaxSpecies || Form != 0 : !game.HasSpecies(Species, Form))
            errors.Add("species_invalid");
        if (Level is < 1 or > 100)
            errors.Add("level_invalid");
        if (Nature is < 0 or > 24)
            errors.Add("nature_invalid");
        if (AbilitySlot is < 0 || AbilitySlot > (gen3 ? 1 : 2))
            errors.Add("ability_invalid");
        if (Ivs is not null && (Ivs.Length != 6 || Ivs.Any(v => v is < 0 or > 31)))
            errors.Add("ivs_invalid");
        if (Evs.Length != 6 || Evs.Any(v => v < 0 || v > (gen3 ? 255 : 252)))
            errors.Add("evs_invalid");
        if (Evs.Sum() > 510)
            errors.Add("evs_total");
        if (Moves.Length > 4 || Moves.Any(m => m > (gen3 ? BuilderData.MaxMove : game.MaxMoveId)))
            errors.Add("moves_invalid");
        if (Moves.Where(m => m != 0).GroupBy(m => m).Any(g => g.Count() > 1))
            errors.Add("moves_repeated");
        if (Nickname is not null && Nickname.Length > (gen3 ? 10 : 12))
            errors.Add("nickname_too_long");
        if (Nickname is not null && Nickname.Any(c => c is '(' or ')' or '@' or '\n' or '\r'))
            errors.Add("nickname_invalid_chars");
        if (Trainer.Name.Length < 1 || Trainer.Name.Length > game.MaxTrainerNameLength)
            errors.Add("ot_invalid");
        if (TrainerIds.FromDisplay(game, Trainer.Tid, Trainer.Sid) is null)
            errors.Add("trainer_id_invalid");
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
        var game = GameInfo;
        var species = s.specieslist[Species];
        if (Form > 0)
        {
            var formName = FormConverter.GetStringFromForm(Species, Form, s, game.Context);
            species += "-" + ShowdownParsing.GetShowdownFormName(Species, formName);
        }
        if (!string.IsNullOrWhiteSpace(Nickname))
            sb.Append($"{Nickname.Trim()} ({species})");
        else
            sb.Append(species);
        if (Gender is "M" or "F")
            sb.Append($" ({Gender})");
        sb.AppendLine();

        if (AbilitySlot is { } slot)
        {
            var ability = game.Family == GameFamily.Frlg
                ? PersonalTable.E[Species].GetAbility(slot == 1)
                : game.PersonalTable.GetFormEntry(Species, Form).GetAbilityAtIndex(slot);
            sb.AppendLine($"Ability: {s.abilitylist[ability]}");
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
        // Da Gen 7 em diante o AutoMod lê TID/SID como o jogo mostra (6 e 4 dígitos) e reempacota.
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

    /// <summary>Descreve um Pokémon existente no mesmo formato (para editar e para comparar).</summary>
    public static PokemonSpec FromPk(PKM pk, TradeGame game = TradeGame.FireRed)
    {
        var speciesName = SpeciesName.GetSpeciesNameGeneration(pk.Species, pk.Language, pk.Format);
        var info = GameCatalog.Of(game);
        var (tid, sid) = TrainerIds.Display(info, pk.TID16, pk.SID16);
        return new PokemonSpec
        {
            Species = pk.Species,
            Form = pk is PK3 ? (byte)0 : pk.Form,
            Nickname = pk.IsEgg || pk.Nickname == speciesName ? null : pk.Nickname,
            Level = pk.CurrentLevel,
            Nature = (int)pk.Nature,
            AbilitySlot = pk is PK3 pk3 ? (pk3.AbilityBit ? 1 : 0) : pk.AbilityNumber switch { 4 => 2, 2 => 1, _ => 0 },
            Gender = pk.Gender switch { 0 => "M", 1 => "F", _ => null },
            Shiny = pk.IsShiny,
            Ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE],
            Evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE],
            Moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4],
            HeldItem = (ushort)pk.HeldItem,
            Ball = pk.Ball,
            Language = pk.Language,
            Game = game,
            Trainer = new TrainerSpec(pk.OriginalTrainerName, tid, sid, pk.OriginalTrainerGender),
        };
    }
}
