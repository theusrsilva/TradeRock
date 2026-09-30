using PKHeX.Core;
using PKHeX.Core.AutoMod;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Builder;

public sealed record ShowdownImportItem(PokemonSpec? Spec, IReadOnlyList<string> Problems, string Text);

/// <summary>Texto do Showdown (um ou vários sets) → pedidos do construtor, para revisar antes de gerar.</summary>
public static class ShowdownImport
{
    // ShowdownSet guarda IVs/EVs na ordem do PKHeX (PS, Atq, Def, Vel, AtqE, DefE).
    private static int[] ToShowdownOrder(int[] pkhex) => [pkhex[0], pkhex[1], pkhex[2], pkhex[4], pkhex[5], pkhex[3]];

    public static IReadOnlyList<ShowdownImportItem> Parse(string text, TrainerSpec trainer, TradeGame game)
    {
        var result = new List<ShowdownImportItem>();
        foreach (var set in ShowdownParsing.GetShowdownSets(text.AsMemory(), BattleTemplateLocalization.Default))
        {
            var problems = set.InvalidLines.Select(l => l.Value ?? "").Where(v => v.Length > 0).ToList();
            if (set.Species is 0 or > BuilderData.MaxSpecies)
            {
                result.Add(new ShowdownImportItem(null, [.. problems, "species_not_gen3"], set.Text));
                continue;
            }

            var pi = PersonalTable.E[set.Species];
            int? abilitySlot = set.Ability < 0 ? null : pi.GetIndexOfAbility(set.Ability) switch { -1 => null, var i => i };
            if (set.Ability >= 0 && abilitySlot is null)
                problems.Add("ability_not_gen3");

            ushort heldItem = 0;
            if (set.HeldItem > 0)
            {
                // Nomes de item do Showdown → ID interno da Gen 3 (pelo nome em inglês).
                var name = GameInfo.GetStrings("en").itemlist[set.HeldItem];
                var match = BuilderData.Items.FirstOrDefault(i => string.Equals(i.Text, name, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    problems.Add("item_not_gen3");
                else
                    heldItem = (ushort)match.Value;
            }

            var regen = new RegenTemplate(set, 3).Regen;
            var moves = set.Moves.Where(m => m != 0).ToArray();
            if (moves.Any(m => m > BuilderData.MaxMove))
                problems.Add("move_not_gen3");

            var spec = new PokemonSpec
            {
                Species = set.Species,
                Nickname = string.IsNullOrWhiteSpace(set.Nickname) ? null : set.Nickname,
                Level = set.Level,
                Nature = set.Nature == Nature.Random ? null : (int)set.Nature,
                AbilitySlot = abilitySlot,
                Gender = set.Gender switch { 0 => "M", 1 => "F", _ => null },
                Shiny = set.Shiny,
                Ivs = ToShowdownOrder(set.IVs),
                Evs = ToShowdownOrder(set.EVs),
                Moves = moves.Where(m => m <= BuilderData.MaxMove).ToArray(),
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
