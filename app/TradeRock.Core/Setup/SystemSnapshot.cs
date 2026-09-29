using TradeRock.Core.Engines.Worker;

namespace TradeRock.Core.Setup;

public sealed record DistroInfo(string Id, string IdLike, string Name);

/// <summary>Uma placa Wi-Fi (phy do kernel) como o Linux a enxerga.</summary>
public sealed record WifiCard(
    string Phy,
    IReadOnlyList<string> Interfaces,
    string? Driver,
    string? Module,
    string Bus, // usb | pci | sdio | other
    string? VendorId,
    string? ProductId,
    string? ProductName,
    bool IsInternetConnection);

/// <summary>Porta serial (ttyUSB/ttyACM no Linux, COMx no Windows).</summary>
public sealed record SerialPortInfo(
    string Path,
    string? VendorId,
    string? ProductId,
    string? ProductName,
    bool DriverMissing = false); // só Windows: dispositivo presente sem driver

/// <summary>Arquivos que tiram as interfaces do LDN do NetworkManager e do renomeador do udev.</summary>
public sealed record LdnNetworkConfig(bool NetworkManager, bool UdevLink)
{
    public bool Complete => NetworkManager && UdevLink;
}

public sealed record UserInfo(string Name, IReadOnlyList<string> Groups, IReadOnlyList<string> SessionGroups);

/// <summary>
/// Tudo o que o assistente precisa saber do sistema, lido uma vez. Vem do sistema real
/// (<see cref="LinuxProbe"/>) ou de um cenário simulado (<see cref="SetupScenarios"/>).
/// </summary>
public sealed record SystemSnapshot(
    string Os, // linux | windows | other
    bool IsWsl,
    string? VirtualMachine,
    DistroInfo? Distro,
    IReadOnlyList<WifiCard> WifiCards,
    IReadOnlyList<SerialPortInfo> SerialPorts,
    UserInfo? User,
    bool BrlttyRunning,
    bool NetworkManagerRunning,
    string? SimulatedScenario = null,
    WorkerInstall? Worker = null,
    LdnNetworkConfig? LdnNetwork = null,
    IReadOnlyList<string>? MissingTools = null)
{
    /// <summary>Ferramentas que o motor com a placa Wi-Fi chama. iw e ip vêm embutidos no worker.</summary>
    public static readonly string[] WifiTools = ["iw", "ip", "sysctl"];

    /// <summary>Grupo que dá acesso às portas seriais: uucp no Arch e derivados, dialout nos demais.</summary>
    public string SerialGroup =>
        Distro is { } d && (d.Id == "arch" || d.IdLike.Split(' ').Contains("arch")) ? "uucp" : "dialout";
}
