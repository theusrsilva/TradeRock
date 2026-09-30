using TradeRock.Core.Engines.Worker;

namespace TradeRock.Core.Setup;

/// <summary>
/// Cenários simulados do assistente (especificação: testes simulando Linux com placa compatível,
/// instável, desconhecida e problemática, WSL, máquina virtual, Linux com ESP32 e Windows com ESP32).
/// Usados nos testes e, em desenvolvimento, no seletor de simulação.
/// </summary>
public static class SetupScenarios
{
    private static readonly DistroInfo Ubuntu = new("ubuntu", "debian", "Ubuntu 24.04 LTS");
    private static readonly DistroInfo Arch = new("arch", "", "Arch Linux");

    private static UserInfo User(params string[] groups) => new("ash", ["ash", .. groups], ["ash", .. groups]);

    private static WifiCard Pci(string phy, string iface, string module, bool internet, string? driver = null) =>
        new(phy, [iface], driver ?? module, module, "pci", null, null, null, internet);

    private static WifiCard Usb(string phy, string iface, string module, string vid, string pid, string name) =>
        new(phy, [iface], module, module, "usb", vid, pid, name, false);

    private static readonly WorkerInstall Worker = new("dev", "/usr/local/lib/traderock-dev/traderock-worker", true, true, true);
    private static readonly LdnNetworkConfig NotConfigured = new(false, false);

    public static IReadOnlyDictionary<string, SystemSnapshot> All { get; } = new Dictionary<string, SystemSnapshot>
    {
        // Notebook com Intel AX200 (internet) e um adaptador USB ALFA ligado.
        ["linux-compatible"] = new("linux", false, null, Ubuntu,
            [Pci("phy0", "wlp2s0", "iwlwifi", internet: true), Usb("phy1", "wlx00c0ca000001", "mt76x0u", "0e8d", "7610", "WiFi")],
            [], User("dialout"), false, true, Worker: Worker, LdnNetwork: NotConfigured, MissingTools: []),

        // Notebook com RZ616 (mt7921e), que é a conexão de internet.
        ["linux-unstable"] = new("linux", false, null, Ubuntu,
            [Pci("phy0", "wlp1s0", "mt7921e", internet: true)],
            [], User("dialout"), false, true, Worker: Worker, LdnNetwork: NotConfigured, MissingTools: []),

        // Placa Broadcom, fora do catálogo; worker ainda não instalado e sem o pacote iw.
        ["linux-unknown"] = new("linux", false, null, Ubuntu,
            [Pci("phy0", "wlp3s0", "brcmfmac", internet: true)],
            [], User(), false, true, LdnNetwork: NotConfigured, MissingTools: ["iw"]),

        // Só a Intel AX200.
        ["linux-problematic"] = new("linux", false, null, Ubuntu,
            [Pci("phy0", "wlp2s0", "iwlwifi", internet: true)],
            [], User("dialout"), false, true, Worker: Worker, LdnNetwork: NotConfigured, MissingTools: []),

        ["linux-no-wifi"] = new("linux", false, null, Ubuntu, [], [], User("dialout"), false, true),

        ["wsl"] = new("linux", true, "desconhecida", Ubuntu, [], [], User("dialout"), false, false),

        ["vm"] = new("linux", false, "VirtualBox", Ubuntu, [], [], User(), false, true),

        // Arch com Intel AX200 e um ESP32-S3 (CH340) no USB; usuário fora do grupo uucp e brltty rodando.
        ["linux-esp32"] = new("linux", false, null, Arch,
            [Pci("phy0", "wlan0", "iwlwifi", internet: true)],
            [new SerialPortInfo("/dev/ttyUSB0", "1a86", "7523", "USB Serial")],
            User("wheel"), true, true),

        // Windows com um conversor CH340 sem driver instalado (sem porta COM) e um ESP32-C3 pela USB nativa.
        ["windows-esp32"] = new("windows", false, null, null, [],
            [new SerialPortInfo(@"USB\VID_1A86&PID_7523\5&2A8F0B1&0&3", "1a86", "7523", "USB-SERIAL CH340", DriverMissing: true),
             new SerialPortInfo("COM4", "303a", "1001", "Dispositivo serial USB")],
            null, false, false),
    }.ToDictionary(kv => kv.Key, kv => kv.Value with { SimulatedScenario = kv.Key });
}
