using System.Text.Json;
using System.Text.Json.Nodes;
using PKHeX.Core;

namespace TradeRock.Core.Builder;

/// <summary>
/// Mensagens de legalidade do PKHeX em português (content/pt-BR/pkhex/*.json, só a Gen 3) por cima
/// do inglês do próprio PKHeX: chave que falta fica em inglês. O prefixo "Invalid:/Valid:" é removido
/// (a interface mostra a gravidade com cor e ícone).
/// </summary>
public static class LegalityLocalizations
{
    private static readonly Lock Lock = new();
    private static readonly Dictionary<string, LegalityLocalizationSet> Cache = [];

    public static IReadOnlyList<string> Supported { get; } = ["pt-BR", "en"];

    public static LegalityLocalizationSet Get(string language)
    {
        language = Supported.Contains(language) ? language : "pt-BR";
        lock (Lock)
        {
            if (Cache.TryGetValue(language, out var cached))
                return cached;

            var en = LegalityLocalizationSet.GetLocalization("en");
            var set = new LegalityLocalizationSet
            {
                Strings = en.Strings, // nomes de espécies, golpes e itens: inglês (decisão do projeto)
                Lines = Overlay(en.Lines, language, "legality", ("F0_1", "{1}")),
                Moves = Overlay(en.Moves, language, "movesource", ("FormatMove", "{2}"), ("FormatRelearn", "{2}")),
                Encounter = Overlay(en.Encounter, language, "encounter"),
                General = Overlay(en.General, language, "general"),
            };
            Cache[language] = set;
            return set;
        }
    }

    /// <summary>Serializa o objeto inglês, aplica o JSON do idioma e os ajustes, e desserializa de volta.</summary>
    private static T Overlay<T>(T english, string language, string file, params (string Key, string Value)[] forced)
    {
        var node = JsonSerializer.SerializeToNode(english)!.AsObject();
        if (language != "en" && ReadResource($"pkhex/{language}/{file}.json") is { } json)
        {
            foreach (var (key, value) in JsonNode.Parse(json)!.AsObject())
            {
                if (node.ContainsKey(key) && value is not null)
                    node[key] = value.DeepClone();
            }
        }
        foreach (var (key, value) in forced)
            node[key] = value;
        return node.Deserialize<T>()!;
    }

    private static string? ReadResource(string name)
    {
        using var stream = typeof(LegalityLocalizations).Assembly.GetManifestResourceStream(name);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
