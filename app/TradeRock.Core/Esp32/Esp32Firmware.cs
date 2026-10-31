using System.Security.Cryptography;

namespace TradeRock.Core.Esp32;

/// <summary>
/// Um binário completo ("merged") do firmware do pokeldn (assets/firmware/pokeldn, Release do pokeldn): bootloader +
/// partições + app, gravado em 0x0. Modo, frequência e tamanho da flash ficam como estão no cabeçalho da imagem
/// ("keep"), como o próprio pokeldn grava (gui/board.py). <see cref="FlashSize"/> é o mínimo que a placa precisa ter.
/// <see cref="Target"/> é o CONFIG_IDF_TARGET, o mesmo nome do campo "chip" do catálogo.
/// </summary>
public sealed record FirmwareImage(string Target, string File, string FlashSize, string Connection)
{
    public const string FlashMode = "keep";
    public const string FlashFreq = "keep";
    public const string FlashSizeParam = "keep";

    /// <summary>Tamanho da flash em MB ("16MB" → 16).</summary>
    public int FlashSizeMb => int.Parse(FlashSize.TrimEnd('M', 'B'));
}

public sealed class FirmwareIntegrityException(string file) : Exception($"Firmware {file} não confere com o SHA256SUMS.txt.");

/// <summary>Os binários embutidos no app, sempre conferidos pelo SHA256SUMS.txt publicado junto.</summary>
public static class Esp32Firmware
{
    // docs/hardware_esp32.md do pokeldn: o ESP32 clássico fala pela UART (conversor USB), os outros pelo USB nativo.
    // O ESP32-S2 não é suportado.
    public static IReadOnlyList<FirmwareImage> Images { get; } =
    [
        new("esp32", "pokeldn-radio.bin", "4MB", "uart"),
        new("esp32c3", "pokeldn-radio-c3.bin", "4MB", "native_usb"),
        new("esp32c6", "pokeldn-radio-c6.bin", "4MB", "native_usb"),
        new("esp32s3", "pokeldn-radio-s3.bin", "4MB", "native_usb"),
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
