using TradeRock.Core.Games;

namespace TradeRock.Core.Trading;

/// <summary>
/// Nome do treinador simulado. No FRLG, o motor codifica o nome na tabela de caracteres Gen III
/// internacional (frlgsim/charmap.py) e exige no máximo 7 caracteres, sem perdas; letras acentuadas
/// (á, ç, ã…) não existem nessa tabela. Os jogos novos usam UTF-16 e aceitam até 12 caracteres.
/// </summary>
public static class TrainerName
{
    public const int MaxLength = 7;

    // Mesmo conjunto do _DEC em frlgsim/charmap.py.
    private const string Punctuation = " .-·…¥×/“”‘’♂♀,!?";

    public static bool IsAllowedChar(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' || Punctuation.Contains(c);

    public static IReadOnlyList<string> Validate(string? name, SwitchGame? game = null)
    {
        if (string.IsNullOrEmpty(name))
            return ["trainer_name_empty"];

        var gen3 = game is null || game.Family == GameFamily.Frlg;
        var errors = new List<string>();
        if (name.Length > (game?.MaxTrainerNameLength ?? MaxLength))
            errors.Add("trainer_name_too_long");
        if (gen3 ? name.Any(c => !IsAllowedChar(c)) : name.Any(char.IsControl))
            errors.Add("trainer_name_invalid_chars");
        return errors;
    }
}
