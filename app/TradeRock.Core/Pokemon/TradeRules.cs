namespace TradeRock.Core.Pokemon;

/// <summary>
/// "Meu treinador": os dados do save do Switch. Usado como OT padrão no construtor e para avisar
/// sobre obediência e Pokédex Nacional. O SID não aparece no jogo; pode vir de um Pokémon recebido.
/// </summary>
public sealed record MyTrainer(string Name, ushort Tid, ushort? Sid, byte Gender = 0, byte Badges = 8, bool HasNationalDex = true);

/// <summary>O mínimo de um Pokémon que as regras precisam.</summary>
public sealed record TradeRuleInput(ushort Species, bool IsEgg, bool Fateful, byte Level, string OtName, ushort Tid, ushort Sid, ushort HeldItem);

public sealed record TradeWarning(string Code, IReadOnlyDictionary<string, object> Args);

/// <summary>
/// Regras do jogo que afetam uma troca, conferidas na pokefirered:
/// - src/trade.c CanTradeSelectedMon: sem a Pokédex Nacional, o jogo não troca espécies acima de
///   #151 (KANTO_SPECIES_END) nem ovos; o lado que envia recusa mandar #152+ para quem não tem a Nacional
///   (o motor do PC não aplica essa regra, por isso o aviso). Mew e Deoxys só são trocáveis com a
///   flag de encontro fatídico.
/// - src/battle_util.c IsMonDisobedient: Pokémon de outro treinador desobedece acima do nível
///   10 / 30 (2ª insígnia) / 50 (4ª) / 70 (6ª); com a 8ª, obedece sempre. Mew/Deoxys sem a flag
///   de encontro fatídico nunca obedecem.
/// - Evolução por troca (<see cref="TradeEvolution"/>).
/// </summary>
public static class TradeRules
{
    public const ushort KantoEnd = 151;

    public static int ObedienceLevel(byte badges) => badges switch
    {
        >= 8 => 100,
        >= 6 => 70,
        >= 4 => 50,
        >= 2 => 30,
        _ => 10,
    };

    public static IReadOnlyList<TradeWarning> For(TradeRuleInput p, MyTrainer? me)
    {
        var warnings = new List<TradeWarning>();

        if (p.Species is 151 or 386 && !p.Fateful)
            warnings.Add(new TradeWarning("mythical_not_fateful", new Dictionary<string, object>()));

        if (p.IsEgg || p.Species > KantoEnd)
        {
            if (me is null)
                warnings.Add(new TradeWarning("national_dex_unknown", Args(("species", p.Species))));
            else if (!me.HasNationalDex)
                warnings.Add(new TradeWarning("national_dex_required", Args(("species", p.Species))));
        }

        if (TradeEvolution.EvolvesInto(p.Species, p.HeldItem) is { } target)
            warnings.Add(new TradeWarning("trade_evolution", Args(("into", target))));

        if (me is not null && !p.IsEgg && IsOtherTrainer(p, me))
        {
            var limit = ObedienceLevel(me.Badges);
            if (p.Level > limit)
                warnings.Add(new TradeWarning("obedience", Args(("level", p.Level), ("limit", limit), ("badges", me.Badges))));
        }

        return warnings;
    }

    /// <summary>IsOtherTrainer da pokefirered compara o ID de 32 bits e o nome.</summary>
    public static bool IsOtherTrainer(TradeRuleInput p, MyTrainer me) =>
        !string.Equals(p.OtName, me.Name, StringComparison.Ordinal)
        || p.Tid != me.Tid
        || (me.Sid is { } sid && p.Sid != sid);

    private static Dictionary<string, object> Args(params (string Key, object Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value);
}
