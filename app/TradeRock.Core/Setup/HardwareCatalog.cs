using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeRock.Core.Setup;

/// <summary>Status de compatibilidade. A ordem importa: do melhor para o pior.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompatStatus>))]
public enum CompatStatus
{
    [JsonStringEnumMemberName("recommended")] Recommended,
    [JsonStringEnumMemberName("works")] Works,
    [JsonStringEnumMemberName("untested")] Untested,
    [JsonStringEnumMemberName("unstable")] Unstable,
    [JsonStringEnumMemberName("problematic")] Problematic,
    [JsonStringEnumMemberName("unsupported")] Unsupported,
}

public sealed record LocalizedText([property: JsonPropertyName("pt-BR")] string PtBr, [property: JsonPropertyName("en")] string En)
{
    public string For(string language) => language == "en" ? En : PtBr;
}

public sealed record WifiEntry(
    string Id, string Model, string Type, string Chip, IReadOnlyList<string> Drivers,
    CompatStatus Status, LocalizedText Notes, string Source, string VerifiedAt);

public sealed record Esp32Entry(
    string Id, string Model, string Chip, string? Module, string Connection, string Flash,
    CompatStatus Status, LocalizedText Notes, string Source, string VerifiedAt);

public sealed record UsbSerialEntry(string Vid, string Pid, string Name, string? DriverUrl);

/// <summary>
/// Catálogo de hardware (catalog/hardware-catalog.json, embutido). Dados, não código: para mudar
/// um status, edite o JSON. Drivers fora do catálogo são "não testado".
/// </summary>
public sealed record HardwareCatalog(
    int Schema,
    string UpdatedAt,
    LocalizedText Notice,
    IReadOnlyList<WifiEntry> Wifi,
    IReadOnlyList<Esp32Entry> Esp32,
    IReadOnlyList<UsbSerialEntry> UsbSerial)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HardwareCatalog Embedded { get; } = LoadEmbedded();

    public static HardwareCatalog Parse(string json) =>
        JsonSerializer.Deserialize<HardwareCatalog>(json, Json) ?? throw new InvalidDataException("Catálogo vazio.");

    private static HardwareCatalog LoadEmbedded()
    {
        using var stream = typeof(HardwareCatalog).Assembly.GetManifestResourceStream("catalog/hardware-catalog.json")
            ?? throw new InvalidOperationException("Catálogo de hardware não embutido.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Entradas que casam com o módulo/driver do kernel. Vários modelos podem usar o mesmo driver
    /// (ex.: iwlwifi); o status considerado é o pior deles, para não prometer demais.
    /// </summary>
    public IReadOnlyList<WifiEntry> MatchWifi(string? module, string? driver) =>
        Wifi.Where(w => w.Drivers.Any(d => string.Equals(d, module, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(d, driver, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    public UsbSerialEntry? MatchUsbSerial(string? vid, string? pid) =>
        UsbSerial.FirstOrDefault(u => string.Equals(u.Vid, vid, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(u.Pid, pid, StringComparison.OrdinalIgnoreCase));
}
