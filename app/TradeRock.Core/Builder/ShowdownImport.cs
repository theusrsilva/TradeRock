using PKHeX.Core;
using PKHeX.Core.AutoMod;
using TradeRock.Core.Games;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Builder;

public sealed record ShowdownImportItem(PokemonSpec? Spec, IReadOnlyList<string> Problems, string Text);

/// <summary>
/// Texto do Showdown (um ou vários sets) → pedidos do construtor, para revisar antes de gerar. No FRLG os códigos
/// de problema terminam em "_not_gen3"; nos jogos novos, em "_not_in_game".
/// </summary>
public static class ShowdownImport
{
    // ShowdownSet guarda IVs/EVs na ordem do PKHeX (PS, Atq, Def, Vel, AtqE, DefE).
    private static int[] ToShowdownOrder(int[] pkhex) => [pkhex[0], pkhex[1], pkhex[2], pkhex[4], pkhex[5], pkhex[3]];

    public static IReadOnlyList<ShowdownImportItem> Parse(string text, TrainerSpec trainer, TradeGame game)
    {
        var info = GameCatalog.Of(game);
        var gen3 = info.Family == GameFamily.Frlg;
        var suffix = gen3 ? "_not_gen3" : "_not_in_game";
        var data = BuilderData.For(info);
        var maxMove = gen3 ? BuilderData.MaxMove : info.MaxMoveId;
        var result = new List<ShowdownImportItem>();
        foreach (var set in ShowdownParsing.GetShowdownSets(text.AsMemory(), BattleTemplateLocalization.Default))
        {
            var problems = set.InvalidLines.Select(l => l.Value ?? "").Where(v => v.Length > 0).ToList();
            var form = gen3 ? (byte)0 : set.Form;
            if (gen3 ? set.Species is 0 or > BuilderData.MaxSpecies : !info.HasSpecies(set.Species, form))
            {
                result.Add(new ShowdownImportItem(null, [.. problems, "species" + suffix], set.Text));
                continue;
            }

            var pi = gen3 ? (IPersonalAbility)PersonalTable.E[set.Species] : info.PersonalTable.GetFormEntry(set.Species, form);
            int? abilitySlot = set.Ability < 0 ? null : pi.GetIndexOfAbility(set.Ability) switch { -1 => null, var i => i };
            if (set.Ability >= 0 && abilitySlot is null)
                problems.Add("ability" + suffix);

            ushort heldItem = 0;
            if (set.HeldItem > 0)
            {
                // Nomes de item do Showdown → ID interno do jogo (pelo nome em inglês).
                var name = GameInfo.GetStrings("en").itemlist[set.HeldItem];
                var match = data.Items.FirstOrDefault(i => string.Equals(i.Text, name, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    problems.Add("item" + suffix);
                else
                    heldItem = (ushort)match.Value;
            }

            var regen = new RegenTemplate(set, info.Format).Regen;
            var moves = set.Moves.Where(m => m != 0).ToArray();
            if (moves.Any(m => m > maxMove))
                problems.Add("move" + suffix);

            var spec = new PokemonSpec
            {
                Species = set.Species,
                Form = form,
                Nickname = string.IsNullOrWhiteSpace(set.Nickname) ? null : set.Nickname,
                Level = set.Level,
                Nature = set.Nature == Nature.Random ? null : (int)set.Nature,
                AbilitySlot = abilitySlot,
                Gender = set.Gender switch { 0 => "M", 1 => "F", _ => null },
                Shiny = set.Shiny,
                Ivs = ToShowdownOrder(set.IVs),
                Evs = ToShowdownOrder(set.EVs),
                Moves = moves.Where(m => m <= maxMove).ToArray(),
                HeldItem = heldItem,
                Ball = regen.Extra.Ball is Ball.None ? null : (int)regen.Extra.Ball,
                Language = regen.Extra.Language is { } lang ? (int)lang : (int)LanguageID.English,
                Game = game,
                Trainer = trainer,
            };
            result.Add(new ShowdownImportItem(spec, problems, set.Text));
        }
        return result;
    }
}
