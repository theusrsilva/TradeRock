using PKHeX.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Games;

namespace TradeRock.Core.Pokemon;

/// <summary>Resultado de mover um Pokémon de um jogo para outro. <see cref="Refusal"/> é um código estável.</summary>
public sealed record HomeTransferResult(PKM? Pokemon, string? Refusal, IReadOnlyList<string> Notes)
{
    public bool Ok => Refusal is null && Pokemon is not null;
}

/// <summary>
/// Move um Pokémon entre jogos como o Pokémon HOME faz, com o conversor do PKHeX (Entrega 14d, D22). Regras e
/// ajustes adaptados do ajudante PKHeX do pokeldn (services/pkhex/Program.cs, Converted; AGPL-3.0):
/// - o Let's Go só manda para o HOME, não recebe;
/// - ovos ficam no próprio jogo;
/// - do FireRed/LeafGreen, só sem item segurado e sem golpe de HM (Pal Park e Poké Transfer recusam);
/// - a espécie (e a forma) tem que existir no jogo de destino;
/// - o treinador de destino vira o treinador atual (o HOME entrega no save para onde move);
/// - golpes que o jogo novo não tem são trocados pela sugestão do PKHeX, como os jogos fazem.
/// O rastreador do HOME fica vazio: o HOME recusa um inventado, e os jogos não o conferem.
/// </summary>
public static class HomeTransfer
{
    /// <summary>HMs da Gen 3: Cut, Fly, Surf, Strength, Waterfall, Flash, Rock Smash e Dive.</summary>
    private static readonly HashSet<ushort> HiddenMoves3 = [15, 19, 57, 70, 127, 148, 249, 291];

    /// <summary>Jogos para onde o Pokémon pode ir (código de recusa por jogo; null = pode).</summary>
    public static IReadOnlyDictionary<GameFamily, string?> Destinations(PKM source, GameFamily from)
    {
        var result = new Dictionary<GameFamily, string?>();
        foreach (var game in GameCatalog.All)
            result[game.Family] = game.Family == from ? "same_game" : Refusal(source, game);
        return result;
    }

    public static HomeTransferResult Move(PKM source, SwitchGame to, ITrainerInfo owner)
    {
        if (Refusal(source, to) is { } refusal)
            return new HomeTransferResult(null, refusal, []);

        var pk = EntityConverter.ConvertToType(source.Clone(), to.EntityType, out var result);
        if (pk is null)
            return new HomeTransferResult(null, "no_route", [result.ToString()]);

        var notes = new List<string>();
        if (pk is IHandlerUpdate handler)
            handler.UpdateHandler(owner);
        else if (pk is PB8 pb8)
            pb8.UpdateHandler(owner);
        pk.ResetPartyStats();
        pk.RefreshChecksum();

        var la = new LegalityAnalysis(pk);
        bool Accepted(LegalityAnalysis check, PKM entity) => check.Valid || LegalityReporter.OnlyHomeTrackerMissing(entity, check);
        // O Sword/Shield quer um Pokémon de outro jogo com treinador atual, mesmo que seja o próprio dono.
        if (!Accepted(la, pk) && pk.CurrentHandler == 0)
        {
            var handled = pk.Clone();
            handled.CurrentHandler = 1;
            handled.HandlingTrainerName = owner.OT;
            handled.HandlingTrainerGender = owner.Gender;
            if (handled is IHandlerLanguage language)
                language.HandlingTrainerLanguage = (byte)owner.Language;
            handled.RefreshChecksum();
            var check = new LegalityAnalysis(handled);
            if (Accepted(check, handled))
                (pk, la) = (handled, check);
        }
        // Golpe que o jogo novo não tem: os jogos do HOME trocam os golpes; fica a sugestão do PKHeX.
        if (!Accepted(la, pk) && la.Info.Moves.Any(m => !m.Valid))
        {
            pk.SetMoveset();
            pk.SetRelearnMoves(new LegalityAnalysis(pk));
            Permit(pk);
            pk.RefreshChecksum();
            la = new LegalityAnalysis(pk);
            notes.Add("moves_replaced");
        }
        return new HomeTransferResult(pk, null, notes);
    }

    private static string? Refusal(PKM source, SwitchGame to)
    {
        if (to.Family == GameFamily.Lgpe || !EntityConverter.IsConvertibleToFormat(source, to.Format))
            return "home_no_destination";
        if (source.IsEgg)
            return "egg";
        if (source is PK3 && source.HeldItem != 0)
            return "frlg_held_item";
        if (source is PK3 && source.Moves.Any(HiddenMoves3.Contains))
            return "frlg_hm_move";
        if (!to.HasSpecies(source.Species, source.Form))
            return "species_not_in_game";
        return null;
    }

    /// <summary>Golpe de TM/TR da sugestão só vale com a marca do registro (TRs do Sword/Shield, Arceus, Z-A).</summary>
    private static void Permit(PKM pk)
    {
        if (pk is ITechRecord record)
            record.SetRecordFlags(pk, TechnicalRecordApplicatorOption.LegalCurrent);
        if (pk is IPlusRecord plus && pk.PersonalInfo is IPermitPlus permit)
            PlusRecordApplicator.SetPlusFlags(plus, pk, permit, PlusRecordApplicatorOption.LegalCurrent);
        if (pk is IMoveShop8Mastery shop)
            shop.SetMoveShopFlags(pk);
    }
}
