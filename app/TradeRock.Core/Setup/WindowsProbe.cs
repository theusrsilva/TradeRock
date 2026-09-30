using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace TradeRock.Core.Setup;

/// <summary>Um dispositivo como o Gerenciador de Dispositivos do Windows o mostra (Win32_PnPEntity).</summary>
public sealed record PnpDevice(string DeviceId, string? Name, uint ErrorCode, string? PnpClass);

/// <summary>
/// Leitura do Windows (só ESP32): portas COM com o fabricante e o modelo do conversor USB-serial, e
/// conversores ligados sem driver (código 28 no Gerenciador de Dispositivos), que não ganham porta COM.
/// </summary>
public static partial class WindowsProbe
{
    /// <summary>"Drivers não instalados" (CM_PROB_FAILED_INSTALL).</summary>
    public const uint DriverNotInstalled = 28;

    [GeneratedRegex(@"VID_([0-9A-F]{4})[&+]PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();

    [GeneratedRegex(@"\((COM[0-9]+)\)")]
    private static partial Regex ComName();

    [SupportedOSPlatform("windows")]
    public static SystemSnapshot Read()
    {
        var devices = new List<PnpDevice>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity " +
                "WHERE PNPClass = 'Ports' OR ConfigManagerErrorCode = " + DriverNotInstalled);
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    devices.Add(new PnpDevice(item["DeviceID"] as string ?? "", item["Name"] as string,
                        item["ConfigManagerErrorCode"] is uint code ? code : 0, item["PNPClass"] as string));
                }
            }
        }
        catch (ManagementException)
        {
            // WMI indisponível: fica só com os nomes das portas.
        }
        return new SystemSnapshot("windows", false, null, null, [], Ports(devices, SerialPort.GetPortNames()), null, false, false);
    }

    /// <summary>Portas COM (com VID/PID quando for USB) e conversores USB sem driver.</summary>
    public static IReadOnlyList<SerialPortInfo> Ports(IEnumerable<PnpDevice> devices, IEnumerable<string> portNames)
    {
        var result = new List<SerialPortInfo>();
        foreach (var d in devices)
        {
            var ids = VidPid().Match(d.DeviceId);
            string? vid = ids.Success ? ids.Groups[1].Value.ToLowerInvariant() : null;
            string? pid = ids.Success ? ids.Groups[2].Value.ToLowerInvariant() : null;

            if (d.ErrorCode == DriverNotInstalled)
            {
                if (vid is not null)
                    result.Add(new SerialPortInfo(d.DeviceId, vid, pid, d.Name, DriverMissing: true));
                continue;
            }
            if (d.Name is { } name && ComName().Match(name) is { Success: true } com)
            {
                var label = name[..com.Index].Trim();
                result.Add(new SerialPortInfo(com.Groups[1].Value, vid, pid, label.Length > 0 ? label : null));
            }
        }

        // Portas que o WMI não mostrou (ex.: WMI desligado): só o nome.
        foreach (var port in portNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!result.Any(p => string.Equals(p.Path, port, StringComparison.OrdinalIgnoreCase)))
                result.Add(new SerialPortInfo(port, null, null, null));
        }

        return result.OrderBy(p => p.DriverMissing).ThenBy(p => ComNumber(p.Path)).ThenBy(p => p.Path, StringComparer.Ordinal).ToList();
    }

    private static int ComNumber(string path) =>
        path.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(path[3..], out var n) ? n : int.MaxValue;
}
