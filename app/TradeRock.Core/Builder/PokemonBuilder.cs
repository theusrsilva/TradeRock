using PKHeX.Core;
using PKHeX.Core.AutoMod;
using TradeRock.Core.Games;
using TradeRock.Core.Pokemon;

namespace TradeRock.Core.Builder;

/// <summary>Campo em que o resultado ficou diferente do pedido (o gerador precisou mudar).</summary>
public sealed record SpecDifference(string Field, string Requested, string Obtained);

/// <summary>O que impede o pedido (quando a geração falha). Code: level_below_minimum, shiny, nature, ivs, moves, ball, ability, gender, origin, combination.</summary>
public sealed record Blocker(string Code, IReadOnlyDictionary<string, object> Args);

public sealed record BuildResult(
    string Generation, // regenerated | failed | timeout | invalid_request
    IReadOnlyList<string> Errors,
    byte[]? Data,
    PokemonSummary? Summary,
    PokemonSpec? Obtained,
    LegalityReport? Legality,
    IReadOnlyList<SpecDifference> Differences,
    EncounterOption? Encounter,
    RawPokemon? Raw = null,
    IReadOnlyList<Blocker>? Blockers = null)
{
    /// <summary>A geração produziu um Pokémon aproveitável (legal ou não verificável).</summary>
    public bool Usable => Generation == "regenerated" && Legality?.Status is LegalityStatus.Legal or LegalityStatus.Unverifiable;
}

/// <summary>
/// Modo automático ("legal por padrão"): o AutoMod procura um encontro válido e calcula PID/IV compatíveis
/// (na Gen 3, natureza, habilidade, gênero e shiny saem do PID, e o PKHeX confere a correlação PID-IV do
/// encontro). Nos jogos novos, o AutoMod gera no formato do jogo escolhido (Entrega 14). O resultado sempre
/// passa pelo LegalityAnalysis.
/// </summary>
public static class PokemonBuilder
{
    /// <summary>Quantos encontros candidatos tentar antes de deixar o AutoMod escolher sozinho.</summary>
    private const int MaxCandidates = 4;

    public static async Task<BuildResult> GenerateAsync(PokemonSpec spec, string language, CancellationToken ct)
    {
        var errors = spec.Validate();
        if (errors.Count > 0)
            return new BuildResult("invalid_request", errors, null, null, null, null, [], null);

        var encounters = BuilderData.Encounters(spec.Species, spec.Game, spec.Form);
        if (spec.EncounterKey is { } key)
        {
            var chosen = encounters.FirstOrDefault(e => e.Key == key);
            if (chosen is null)
                return new BuildResult("invalid_request", ["encounter_invalid"], null, null, null, null, [], null);
            return await AttemptAsync(spec, chosen, language, ct);
        }

        // Origem automática: o AutoMod pega o primeiro encontro que funciona na ordem do PKHeX, que põe
        // eventos antes dos encontros do jogo. Aqui os candidatos vêm na ordem do TradeRock (jogo escolhido,
        // outro FRLG, RSE, Colosseum/XD; eventos por último) e fica o resultado legal mais fiel ao pedido.
        BuildResult? best = null;
        foreach (var candidate in encounters.Take(MaxCandidates))
        {
            var r = await AttemptAsync(spec, candidate, language, ct);
            if (IsBetter(r, best))
                best = r;
            if (best is { Legality.Status: LegalityStatus.Legal, Differences.Count: 0 })
                return best;
        }

        var free = await AttemptAsync(spec, null, language, ct);
        var result = IsBetter(free, best) ? free : best!;
        if (result.Usable)
            return result;

        // Falhou: descobrir o que atrapalha, para a tela dizer o que afrouxar.
        return result with { Blockers = await DiagnoseAsync(spec, encounters, language, ct) };
    }

    /// <summary>
    /// Tenta de novo afrouxando um pedido por vez. O que, sozinho, faz dar certo é um "bloqueio".
    /// Tentativas sem escolher encontro (rápidas), no máximo uma por campo pedido.
    /// </summary>
    private static async Task<IReadOnlyList<Blocker>> DiagnoseAsync(
        PokemonSpec spec, IReadOnlyList<EncounterOption> encounters, string language, CancellationToken ct)
    {
        var blockers = new List<Blocker>();
        if (encounters.Count == 0)
            return [new Blocker("species_unobtainable", new Dictionary<string, object>())];

        var pool = spec.EncounterKey is { } key ? encounters.Where(e => e.Key == key).ToList() : encounters.ToList();
        var minLevel = pool.Count > 0 ? pool.Min(e => e.LevelMin) : (byte)1;
        var baseSpec = spec;
        if (spec.Level < minLevel)
        {
            blockers.Add(new Blocker("level_below_minimum", new Dictionary<string, object> { ["min"] = minLevel }));
            baseSpec = spec with { Level = minLevel };
            if ((await AttemptAsync(baseSpec, null, language, ct)).Usable)
                return blockers;
        }

        var relaxations = new List<(string Code, bool Requested, PokemonSpec Relaxed)>
        {
            ("shiny", baseSpec.Shiny, baseSpec with { Shiny = false }),
            ("nature", baseSpec.Nature is not null, baseSpec with { Nature = null }),
            ("ivs", baseSpec.Ivs is not null, baseSpec with { Ivs = null }),
            ("moves", baseSpec.Moves.Any(m => m != 0), baseSpec with { Moves = [] }),
            ("ball", baseSpec.Ball is not null, baseSpec with { Ball = null }),
            ("ability", baseSpec.AbilitySlot is not null, baseSpec with { AbilitySlot = null }),
            ("gender", baseSpec.Gender is not null, baseSpec with { Gender = null }),
            ("origin", baseSpec.EncounterKey is not null, baseSpec with { EncounterKey = null }),
        };

        foreach (var (code, requested, relaxed) in relaxations.Where(r => r.Requested))
        {
            if ((await AttemptAsync(relaxed, null, language, ct)).Usable)
                blockers.Add(new Blocker(code, new Dictionary<string, object>()));
        }

        if (blockers.Count == 0)
        {
            var all = baseSpec with { Shiny = false, Nature = null, Ivs = null, Moves = [], Ball = null, AbilitySlot = null, Gender = null, EncounterKey = null };
            if ((await AttemptAsync(all, null, language, ct)).Usable)
                blockers.Add(new Blocker("combination", new Dictionary<string, object>()));
        }
        return blockers;
    }

    private static bool IsBetter(BuildResult candidate, BuildResult? current)
    {
        if (current is null)
            return true;
        static int Rank(BuildResult r) => r.Legality?.Status switch
        {
            LegalityStatus.Legal => 0,
            LegalityStatus.Unverifiable => 1,
            _ => 2,
        };
        var (a, b) = (Rank(candidate), Rank(current));
        return a != b ? a < b : candidate.Differences.Count < current.Differences.Count;
    }

    private static async Task<BuildResult> AttemptAsync(PokemonSpec spec, EncounterOption? encounter, string language, CancellationToken ct)
    {
        var game = spec.GameInfo;
        var target = GameCatalog.ToVersion(spec.Game);
        var text = spec.ToRegenText(encounter);
        var ids = TrainerIds.FromDisplay(game, spec.Trainer.Tid, spec.Trainer.Sid) ?? (0, 0);
        var (created, status) = await AutoModEnvironment.RunAsync(target, () =>
        {
            var set = new RegenTemplate(new ShowdownSet(text), game.Format);
            var tr = new SimpleTrainerInfo(target)
            {
                OT = spec.Trainer.Name,
                TID16 = ids.Tid16,
                SID16 = ids.Sid16,
                Gender = spec.Trainer.Gender,
                Language = spec.Language,
            };
            var result = tr.GetLegalFromSet(set);
            return (result.Created, result.Status);
        }, ct);

        // O AutoMod pode devolver outro formato quando falha antes de converter.
        var pk = created?.GetType() == game.EntityType
            ? created
            : (created is null ? null : EntityConverter.ConvertToType(created, game.EntityType, out _)) ?? Fallback(game, spec);

        if (spec.HeldItem != 0)
        {
            pk.HeldItem = spec.HeldItem;
            pk.RefreshChecksum();
        }
        return Finish(pk, spec, status.ToString().ToLowerInvariant(), language);
    }

    private static PKM Fallback(SwitchGame game, PokemonSpec spec)
    {
        var pk = game.Blank();
        pk.Species = spec.Species;
        pk.Form = spec.Form;
        return pk;
    }

    internal static BuildResult Finish(PKM pk, PokemonSpec requested, string generation, string language)
    {
        var legality = LegalityReporter.Analyze(pk, language);
        var game = GameCatalog.Of(requested.Game);
        byte[] data;
        if (pk is PK3 pk3)
        {
            data = new byte[Pk3Reader.PartySize];
            if (!pk3.PartyStatsPresent)
                pk3.ResetPartyStats();
            pk3.WriteDecryptedDataParty(data);
        }
        else
        {
            // Jogos novos: a forma "de caixa", como o cofre guarda (PokemonReader).
            pk.ResetPartyStats();
            pk.RefreshChecksum();
            data = new byte[pk.SIZE_STORED];
            pk.WriteDecryptedDataStored(data);
        }

        var summary = PokemonReader.Summarize(pk, game.Family);
        var obtained = PokemonSpec.FromPk(pk, requested.Game);
        var encounter = legality.Status != LegalityStatus.Illegal
            ? BuilderData.ToOption(new LegalityAnalysis(pk).EncounterOriginal, pk.Format)
            : null;

        return new BuildResult(generation, [], data, summary, obtained, legality, Compare(requested, obtained), encounter,
            pk is PK3 raw ? RawPokemon.FromPk(raw) : null);
    }

    /// <summary>O que ficou diferente do pedido (só campos que a pessoa definiu).</summary>
    public static IReadOnlyList<SpecDifference> Compare(PokemonSpec req, PokemonSpec got)
    {
        var diff = new List<SpecDifference>();
        var s = BuilderData.Strings;

        void Check(string field, bool requested, string want, string have)
        {
            if (requested && want != have)
                diff.Add(new SpecDifference(field, want, have));
        }

        Check("species", true, s.specieslist[req.Species], s.specieslist[got.Species]);
        Check("form", req.Form != 0, req.Form.ToString(), got.Form.ToString());
        Check("level", true, req.Level.ToString(), got.Level.ToString());
        Check("nature", req.Nature is not null, Name(s.natures, req.Nature), Name(s.natures, got.Nature));
        Check("ability", req.AbilitySlot is not null, req.AbilitySlot.ToString() ?? "", got.AbilitySlot.ToString() ?? "");
        Check("gender", req.Gender is not null, req.Gender ?? "", got.Gender ?? "");
        Check("shiny", true, req.Shiny ? "yes" : "no", got.Shiny ? "yes" : "no");
        Check("ivs", req.Ivs is not null, string.Join('/', req.Ivs ?? []), string.Join('/', got.Ivs ?? []));
        Check("evs", true, string.Join('/', req.Evs), string.Join('/', got.Evs));
        var wantMoves = req.Moves.Where(m => m != 0).Order().Select(m => s.movelist[m]);
        var haveMoves = got.Moves.Where(m => m != 0).Order().Select(m => s.movelist[m]);
        Check("moves", req.Moves.Any(m => m != 0), string.Join(", ", wantMoves), string.Join(", ", haveMoves));
        Check("ball", req.Ball is > 0, Name(BallNames, req.Ball), Name(BallNames, got.Ball));
        Check("language", true, ((LanguageID)req.Language).ToString(), ((LanguageID)got.Language).ToString());
        Check("trainer", true, $"{req.Trainer.Name} {req.Trainer.Tid}:{req.Trainer.Sid}",
            $"{got.Trainer.Name} {got.Trainer.Tid}:{got.Trainer.Sid}");
        return diff;
    }

    private static readonly string[] BallNames = Enum.GetNames<Ball>();

    private static string Name(IReadOnlyList<string> list, int? index) =>
        index is { } i && i >= 0 && i < list.Count ? list[i] : "";
}
