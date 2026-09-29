namespace TradeRock.Core.Trading;

/// <summary>
/// Nome do treinador simulado. O motor codifica o nome na tabela de caracteres Gen III
/// internacional (frlgsim/charmap.py) e exige no máximo 7 caracteres, sem perdas.
/// Letras acentuadas (á, ç, ã…) não existem nessa tabela.
/// </summary>
public static class TrainerName
{
    public const int MaxLength = 7;

    // Mesmo conjunto do _DEC em frlgsim/charmap.py.
    private const string Punctuation = " .-·…¥×/“”‘’♂♀,!?";

    public static bool IsAllowedChar(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' || Punctuation.Contains(c);

    public static IReadOnlyList<string> Validate(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return ["trainer_name_empty"];

        var errors = new List<string>();
        if (name.Length > MaxLength)
            errors.Add("trainer_name_too_long");
        if (name.Any(c => !IsAllowedChar(c)))
            errors.Add("trainer_name_invalid_chars");
        return errors;
    }
}
