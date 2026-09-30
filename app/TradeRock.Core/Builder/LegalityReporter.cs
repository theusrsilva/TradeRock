using System.Text.Json;
using PKHeX.Core;

namespace TradeRock.Core.Builder;

public enum LegalityStatus
{
    Legal,
    Illegal,
    Unverifiable,
}

/// <summary>Uma verificação do PKHeX, já traduzida. <see cref="Field"/> liga ao campo do formulário.</summary>
public sealed record LegalityCheck(
    string Identifier,
    string Severity,
    string Code,
    string Message,
    string? Field,
    int? MoveIndex);

public sealed record LegalityReport(
    LegalityStatus Status,
    IReadOnlyList<LegalityCheck> Checks,
    string? Encounter,
    string? EncounterLocation,
    string? UnverifiableReason,
    string PkhexVersion)
{
    /// <summary>Só os problemas (inválidos e suspeitos).</summary>
    public IEnumerable<LegalityCheck> Problems => Checks.Where(c => c.Severity != "valid");
}

/// <summary>
/// Roda o LegalityAnalysis do PKHeX e devolve um relatório estruturado e traduzido.
/// "Não verificável" (nunca "legal") quando o PKHeX não acha o encontro de um Pokémon de evento
/// cuja espécie está em Builder/Data/unverifiable.json (hoje vazio: ver o arquivo).
/// </summary>
public static class LegalityReporter
{
    public static string PkhexVersion { get; } =
        typeof(LegalityAnalysis).Assembly.GetName().Version?.ToString() ?? "?";

    private static UnverifiableRule Rule = UnverifiableRule.Load();

    /// <summary>Só para testes: troca a lista de espécies "não verificáveis" até o Dispose.</summary>
    internal static IDisposable OverrideUnverifiableSpecies(params ushort[] species)
    {
        var previous = Rule;
        Rule = new UnverifiableRule { Species = [.. species], Reason = previous.Reason };
        return new Restore(() => Rule = previous);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    public static LegalityReport Analyze(PKM pk, string language = "pt-BR")
    {
        AutoModEnvironment.EnsureInitialized(); // base de eventos carregada
        var la = new LegalityAnalysis(pk);
        var ctx = LegalityLocalizationContext.Create(la, LegalityLocalizations.Get(language));

        var checks = new List<LegalityCheck>();
        foreach (var r in la.Results)
        {
            if (!r.IsNotGeneric() && r.Valid)
                continue; // "Válido." genérico não ajuda ninguém
            checks.Add(new LegalityCheck(
                r.Identifier.ToString(),
                SeverityName(r.Judgement),
                r.Result.ToString(),
                ctx.Humanize(r),
                FieldFor(r.Identifier),
                null));
        }

        var moves = la.Info.Moves;
        for (var i = 0; i < moves.Length; i++)
        {
            var m = moves[i];
            if (m.Valid)
                continue;
            checks.Add(new LegalityCheck("CurrentMove", "invalid", "Move", m.Summary(ctx), "moves", i));
        }

        string? encounter = null, location = null;
        if (la.Valid || la.EncounterOriginal is not null)
        {
            var enc = la.EncounterOriginal;
            encounter = enc.GetEncounterName(ctx.Strings.specieslist);
            location = enc.GetEncounterLocation();
        }

        var status = la.Valid ? LegalityStatus.Legal : LegalityStatus.Illegal;
        string? reason = null;
        if (!la.Valid && Rule.Applies(pk, la))
        {
            status = LegalityStatus.Unverifiable;
            reason = Rule.Reason;
        }

        return new LegalityReport(status, checks, encounter, location, reason, PkhexVersion);
    }

    private static string SeverityName(Severity s) => s switch
    {
        Severity.Invalid => "invalid",
        Severity.Fishy => "fishy",
        _ => "valid",
    };

    /// <summary>Campo do formulário do construtor relacionado a cada grupo de verificação.</summary>
    public static string? FieldFor(CheckIdentifier id) => id switch
    {
        CheckIdentifier.CurrentMove or CheckIdentifier.RelearnMove => "moves",
        CheckIdentifier.Encounter or CheckIdentifier.GameOrigin => "origin",
        CheckIdentifier.Shiny => "shiny",
        CheckIdentifier.PID or CheckIdentifier.EC => "pid",
        CheckIdentifier.Gender => "gender",
        CheckIdentifier.EVs => "evs",
        CheckIdentifier.IVs => "ivs",
        CheckIdentifier.Language => "language",
        CheckIdentifier.Nickname => "nickname",
        CheckIdentifier.Trainer => "trainer",
        CheckIdentifier.Level => "level",
        CheckIdentifier.Ball => "ball",
        CheckIdentifier.Form => "species",
        CheckIdentifier.Egg => "species",
        CheckIdentifier.Fateful => "fateful",
        CheckIdentifier.Ribbon or CheckIdentifier.RibbonMark => "ribbons",
        CheckIdentifier.Ability => "ability",
        CheckIdentifier.Evolution => "species",
        CheckIdentifier.Nature => "nature",
        CheckIdentifier.HeldItem => "heldItem",
        CheckIdentifier.TrashBytes => "nickname",
        _ => null,
    };

    private sealed class UnverifiableRule
    {
        public required HashSet<ushort> Species { get; init; }
        public required string Reason { get; init; }

        // Sem encontro correspondente (os códigos que o PKHeX usa quando "não conhece" a origem).
        private static readonly HashSet<LegalityCheckResultCode> NoMatch =
        [
            LegalityCheckResultCode.EncInvalid,
            LegalityCheckResultCode.EncGiftNotFound,
            LegalityCheckResultCode.FatefulGiftMissing,
        ];

        public bool Applies(PKM pk, LegalityAnalysis la) =>
            Species.Contains(pk.Species)
            && pk.FatefulEncounter
            && la.Results.Any(r => !r.Valid && NoMatch.Contains(r.Result));

        public static UnverifiableRule Load()
        {
            using var stream = typeof(LegalityReporter).Assembly.GetManifestResourceStream("builder/unverifiable.json")!;
            using var doc = JsonDocument.Parse(stream);
            var species = doc.RootElement.GetProperty("species").EnumerateArray().Select(e => e.GetUInt16()).ToHashSet();
            var reason = doc.RootElement.GetProperty("reason").GetString() ?? "";
            return new UnverifiableRule { Species = species, Reason = reason };
        }
    }
}
