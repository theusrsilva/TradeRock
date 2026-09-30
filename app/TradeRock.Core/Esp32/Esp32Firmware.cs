using System.Security.Cryptography;

namespace TradeRock.Core.Esp32;

/// <summary>
/// Um binário completo de firmware do easyworld (engines/frlg-ldn-trade-esp32/firmware/release):
/// bootloader + tabela de partições + app, gravado em 0x0, DIO / 80 MHz.
/// <see cref="Target"/> é o CONFIG_IDF_TARGET, o mesmo nome que o firmware devolve no LDN_HELLO e o
/// campo "chip" do catálogo.
/// </summary>
public sealed record FirmwareImage(string Target, string File, string FlashSize, string Connection)
{
    public const string FlashMode = "dio";
    public const string FlashFreq = "80m";

    /// <summary>Tamanho da flash em MB ("16MB" → 16).</summary>
    public int FlashSizeMb => int.Parse(FlashSize.TrimEnd('M', 'B'));
}

public sealed class FirmwareIntegrityException(string file) : Exception($"Firmware {file} não confere com o SHA256SUMS.txt.");

/// <summary>Os binários embutidos no app, sempre conferidos pelo SHA256SUMS.txt publicado junto.</summary>
public static class Esp32Firmware
{
    // Tabela de firmware/release/README.md do easyworld (chip, flash e interface de cada arquivo).
    public static IReadOnlyList<FirmwareImage> Images { get; } =
    [
        new("esp32", "frlg-trade-esp32-uart-4mb-full.bin", "4MB", "uart"),
        new("esp32c3", "frlg-trade-esp32c3-usb-4mb-full.bin", "4MB", "native_usb"),
        new("esp32c6", "frlg-trade-esp32c6-uart-16mb-full.bin", "16MB", "uart"),
        new("esp32s3", "frlg-trade-esp32s3-uart-16mb-full.bin", "16MB", "uart"),
    ];

    public static FirmwareImage? For(string? target) => Images.FirstOrDefault(i => i.Target == target);

    /// <summary>Nome para a pessoa: esp32s3 → ESP32-S3, esp32 → ESP32.</summary>
    public static string DisplayName(string? target) =>
        target is null ? "?" : target == "esp32" ? "ESP32" : target.StartsWith("esp32", StringComparison.Ordinal)
            ? "ESP32-" + target[5..].ToUpperInvariant() : target.ToUpperInvariant();

    private static Stream Open(string name) =>
        typeof(Esp32Firmware).Assembly.GetManifestResourceStream($"firmware/{name}")
        ?? throw new FileNotFoundException($"Firmware embutido ausente: {name}");

    /// <summary>SHA256 publicados (arquivo → hash em hexadecimal minúsculo).</summary>
    public static IReadOnlyDictionary<string, string> PublishedHashes()
    {
        using var reader = new StreamReader(Open("SHA256SUMS.txt"));
        return reader.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split("  ", 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[1].Trim(), parts => parts[0].Trim().ToLowerInvariant());
    }

    /// <summary>Lê o binário e confere o SHA256; nunca devolve um arquivo que não confere.</summary>
    public static byte[] Read(FirmwareImage image)
    {
        using var stream = Open(image.File);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!PublishedHashes().TryGetValue(image.File, out var expected) || expected != hash)
            throw new FirmwareIntegrityException(image.File);
        return data;
    }

    /// <summary>Grava o binário conferido numa pasta (para o esptool, que lê de arquivo).</summary>
    public static string ExtractTo(FirmwareImage image, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, image.File);
        File.WriteAllBytes(path, Read(image));
        return path;
    }
}
