namespace TradeRock.Core.Pokemon;

/// <summary>
/// Pokémon de demonstração embutidos (engines/frlg-ldn-trade-esp32/assets/party: MEWTWO e DEOXYS,
/// validados pelo autor do easyworld). Usados no modo demonstração e nos testes.
/// </summary>
public static class DemoPokemon
{
    public static IReadOnlyList<(string Name, byte[] Data)> All { get; } = Load();

    private static List<(string, byte[])> Load()
    {
        var assembly = typeof(DemoPokemon).Assembly;
        var result = new List<(string, byte[])>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("demo/")).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            result.Add((Path.GetFileName(name), buffer.ToArray()));
        }
        return result;
    }
}
