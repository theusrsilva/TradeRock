using System.Text;
using System.Text.Json;
using TradeRock.Core;
using TradeRock.Core.Setup;

namespace TradeRock.Tests;

/// <summary>Árvore falsa de /sys, /proc e /etc, com links relativos como no sysfs real.</summary>
internal sealed class FakeLinuxRoot : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("traderock-fakefs-").FullName;

    public FakeLinuxRoot File(string path, string content)
    {
        var full = Path.Combine(Root, path.TrimStart('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return this;
    }

    public FakeLinuxRoot Dir(string path)
    {
        Directory.CreateDirectory(Path.Combine(Root, path.TrimStart('/')));
        return this;
    }

    /// <summary>Link simbólico em <paramref name="path"/> apontando para <paramref name="target"/> (relativo, como no /sys).</summary>
    public FakeLinuxRoot Link(string path, string target)
    {
        var full = Path.Combine(Root, path.TrimStart('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.CreateSymbolicLink(full, target);
        return this;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    /// <summary>Notebook com RTL8821CE (PCI, internet) e ALFA (USB), um CH340 e NetworkManager.</summary>
    public static FakeLinuxRoot Laptop()
    {
        var fs = new FakeLinuxRoot();
        const string pci = "/sys/devices/pci0000:00/0000:00:1c.0/0000:02:00.0";
        const string usbWifi = "/sys/devices/pci0000:00/0000:00:14.0/usb1/1-2";
        const string usbSerial = "/sys/devices/pci0000:00/0000:00:14.0/usb1/1-3";
        fs.File("/proc/version", "Linux version 6.8.0-45-generic (buildd@lcy02) #45-Ubuntu SMP")
          .File("/proc/cpuinfo", "processor\t: 0\nflags\t\t: fpu vme de pse sse sse2\n")
          .File("/sys/class/dmi/id/sys_vendor", "LENOVO\n")
          .File("/sys/class/dmi/id/product_name", "82K2\n")
          .File("/etc/os-release", "NAME=\"Ubuntu\"\nID=ubuntu\nID_LIKE=debian\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\n")
          .File("/proc/net/route", "Iface\tDestination\tGateway\tFlags\nwlp2s0\t00000000\t0101A8C0\t0003\nwlp2s0\t0001A8C0\t00000000\t0001\n")
          // PCI: RTL8821CE (driver registrado como rtw_8821ce, módulo rtw88_8821ce)
          .File($"{pci}/vendor", "0x10ec\n").File($"{pci}/device", "0xc821\n")
          .Dir($"{pci}/net/wlp2s0").Dir($"{pci}/ieee80211/phy0")
          .Dir("/sys/bus/pci/drivers/rtw_8821ce").Link("/sys/bus/pci/drivers/rtw_8821ce/module", "../../../../module/rtw88_8821ce").Dir("/sys/module/rtw88_8821ce")
          .Link($"{pci}/driver", "../../../../bus/pci/drivers/rtw_8821ce")
          .Link($"{pci}/ieee80211/phy0/device", "../../../0000:02:00.0")
          .Link("/sys/class/ieee80211/phy0", "../../devices/pci0000:00/0000:00:1c.0/0000:02:00.0/ieee80211/phy0")
          // USB: ALFA (mt76x0u); o phy aponta para a interface 1-2:1.0, os IDs ficam em 1-2
          .File($"{usbWifi}/idVendor", "0e8d\n").File($"{usbWifi}/idProduct", "7610\n").File($"{usbWifi}/product", "WiFi\n")
          .Dir($"{usbWifi}/1-2:1.0/net/wlx00c0ca000001").Dir($"{usbWifi}/1-2:1.0/ieee80211/phy1")
          .Dir("/sys/bus/usb/drivers/mt76x0u").Link("/sys/bus/usb/drivers/mt76x0u/module", "../../../../module/mt76x0u").Dir("/sys/module/mt76x0u")
          .Link($"{usbWifi}/1-2:1.0/driver", "../../../../../../bus/usb/drivers/mt76x0u")
          .Link($"{usbWifi}/1-2:1.0/ieee80211/phy1/device", "../../../1-2:1.0")
          .Link("/sys/class/ieee80211/phy1", "../../devices/pci0000:00/0000:00:14.0/usb1/1-2/1-2:1.0/ieee80211/phy1")
          // Serial: CH340 em /dev/ttyUSB0
          .File($"{usbSerial}/idVendor", "1a86\n").File($"{usbSerial}/idProduct", "7523\n").File($"{usbSerial}/product", "USB Serial\n")
          .Dir($"{usbSerial}/1-3:1.0/ttyUSB0/tty/ttyUSB0")
          .Link("/sys/class/tty/ttyUSB0", "../../devices/pci0000:00/0000:00:14.0/usb1/1-3/1-3:1.0/ttyUSB0/tty/ttyUSB0")
          .Link($"{usbSerial}/1-3:1.0/ttyUSB0/tty/ttyUSB0/device", "../../../ttyUSB0")
          .Dir("/sys/class/tty/tty0")
          // Usuário: no grupo dialout em /etc/group, mas a sessão ainda não tem o grupo (precisa sair e entrar)
          .File("/etc/passwd", "root:x:0:0::/root:/bin/bash\nash:x:1000:1000::/home/ash:/bin/bash\n")
          .File("/etc/group", "root:x:0:\ndialout:x:20:ash\nsudo:x:27:ash\nash:x:1000:\n")
          .File("/proc/self/status", "Name:\tdotnet\nGroups:\t27 1000 \n")
          .File("/proc/812/comm", "NetworkManager\n")
          .File("/proc/901/comm", "bash\n");
        return fs;
    }
}

public sealed class LinuxProbeTests
{
    [Fact]
    public void Le_placas_driver_modulo_barramento_ids_e_internet()
    {
        using var fs = FakeLinuxRoot.Laptop();
        var snap = new LinuxProbe(fs.Root).Read("ash");

        Assert.False(snap.IsWsl);
        Assert.Null(snap.VirtualMachine);
        Assert.Equal("ubuntu", snap.Distro!.Id);
        Assert.Equal("dialout", snap.SerialGroup);

        var phy0 = snap.WifiCards.Single(c => c.Phy == "phy0");
        Assert.Equal("rtw_8821ce", phy0.Driver);
        Assert.Equal("rtw88_8821ce", phy0.Module);
        Assert.Equal("pci", phy0.Bus);
        Assert.Equal(("10ec", "c821"), (phy0.VendorId, phy0.ProductId));
        Assert.Equal(["wlp2s0"], phy0.Interfaces);
        Assert.True(phy0.IsInternetConnection);

        var phy1 = snap.WifiCards.Single(c => c.Phy == "phy1");
        Assert.Equal("mt76x0u", phy1.Module);
        Assert.Equal("usb", phy1.Bus);
        Assert.Equal(("0e8d", "7610"), (phy1.VendorId, phy1.ProductId));
        Assert.False(phy1.IsInternetConnection);

        var tty = Assert.Single(snap.SerialPorts);
        Assert.Equal("/dev/ttyUSB0", tty.Path);
        Assert.Equal(("1a86", "7523"), (tty.VendorId, tty.ProductId));

        Assert.Contains("dialout", snap.User!.Groups);
        Assert.DoesNotContain("dialout", snap.User.SessionGroups);
        Assert.True(snap.NetworkManagerRunning);
        Assert.False(snap.BrlttyRunning);
    }

    [Theory]
    [InlineData("QEMU", "Standard PC (Q35 + ICH9, 2009)", "QEMU/KVM")]
    [InlineData("innotek GmbH", "VirtualBox", "VirtualBox")]
    [InlineData("VMware, Inc.", "VMware Virtual Platform", "VMware")]
    [InlineData("Microsoft Corporation", "Virtual Machine", "Hyper-V")]
    public void Detecta_maquina_virtual_pelo_dmi(string vendor, string product, string expected)
    {
        using var fs = new FakeLinuxRoot().File("/sys/class/dmi/id/sys_vendor", vendor).File("/sys/class/dmi/id/product_name", product);
        Assert.Equal(expected, new LinuxProbe(fs.Root).DetectVirtualMachine());
    }

    [Fact]
    public void Detecta_wsl_e_flag_hypervisor()
    {
        using var fs = new FakeLinuxRoot()
            .File("/proc/version", "Linux version 6.6.36.6-microsoft-standard-WSL2")
            .File("/proc/cpuinfo", "flags\t\t: fpu sse hypervisor\n");
        var probe = new LinuxProbe(fs.Root);
        Assert.True(probe.IsWsl());
        Assert.Equal("desconhecida", probe.DetectVirtualMachine());
    }

    [Fact]
    public void Arch_usa_o_grupo_uucp()
    {
        Assert.Equal("uucp", SetupScenarios.All["linux-esp32"].SerialGroup);
        var manjaro = SetupScenarios.All["wsl"] with { Distro = new DistroInfo("manjaro", "arch", "Manjaro") };
        Assert.Equal("uucp", manjaro.SerialGroup);
    }

    [Fact]
    public void Sistema_sem_nada_nao_quebra()
    {
        using var fs = new FakeLinuxRoot();
        var snap = new LinuxProbe(fs.Root).Read("ninguem");
        Assert.Empty(snap.WifiCards);
        Assert.Empty(snap.SerialPorts);
        Assert.Null(snap.Distro);
    }
}

public sealed class HardwareCatalogTests
{
    private static HardwareCatalog Catalog => HardwareCatalog.Embedded;

    [Fact]
    public void Toda_entrada_tem_fonte_data_e_notas_nos_dois_idiomas()
    {
        Assert.NotEmpty(Catalog.Wifi);
        Assert.NotEmpty(Catalog.Esp32);
        foreach (var e in Catalog.Wifi.Select(w => (w.Source, w.VerifiedAt, w.Notes)).Concat(Catalog.Esp32.Select(e => (e.Source, e.VerifiedAt, e.Notes))))
        {
            Assert.StartsWith("https://github.com/", e.Source);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", e.VerifiedAt);
            Assert.False(string.IsNullOrWhiteSpace(e.Notes.PtBr));
            Assert.False(string.IsNullOrWhiteSpace(e.Notes.En));
        }
    }

    [Theory]
    [InlineData("mt76x0u", CompatStatus.Recommended)]
    [InlineData("rtw88_8821ce", CompatStatus.Recommended)]
    [InlineData("rtw88_8822ce", CompatStatus.Untested)]
    [InlineData("mt7921e", CompatStatus.Unstable)]
    [InlineData("iwlwifi", CompatStatus.Problematic)]
    [InlineData("ath9k_htc", CompatStatus.Problematic)]
    [InlineData("brcmfmac", CompatStatus.Untested)] // fora do catálogo
    public void Status_por_driver(string module, CompatStatus expected)
    {
        var card = new WifiCard("phy0", ["wlan0"], module, module, "pci", null, null, null, false);
        Assert.Equal(expected, RadioAdvisor.Assess(card, Catalog).Status);
    }

    [Fact]
    public void Driver_registrado_com_outro_nome_tambem_casa()
    {
        var card = new WifiCard("phy0", ["wlan0"], "rtw_8821ce", null, "pci", null, null, null, false);
        Assert.Equal(CompatStatus.Recommended, RadioAdvisor.Assess(card, Catalog).Status);
    }

    [Fact]
    public void Conversores_usb_serial()
    {
        Assert.Equal("WCH CH340", Catalog.MatchUsbSerial("1a86", "7523")!.Name);
        Assert.Equal("Silicon Labs CP210x", Catalog.MatchUsbSerial("10C4", "EA60")!.Name);
        Assert.Null(Catalog.MatchUsbSerial("dead", "beef"));
    }
}

public sealed class RadioAdvisorTests
{
    private static RadioAdvice Advise(string scenario) => RadioAdvisor.Advise(SetupScenarios.All[scenario], HardwareCatalog.Embedded);

    [Theory]
    [InlineData("linux-compatible", "wifi_compatible", "wifi", true)]
    [InlineData("linux-unstable", "wifi_test_first", "wifi", true)]
    [InlineData("linux-unknown", "wifi_test_first", "wifi", true)]
    [InlineData("linux-problematic", "wifi_problematic", "esp32", false)]
    [InlineData("linux-no-wifi", "no_wifi", "esp32", false)]
    [InlineData("wsl", "wsl", "esp32", false)]
    [InlineData("vm", "vm", "esp32", false)]
    [InlineData("linux-esp32", "wifi_problematic", "esp32", false)]
    [InlineData("windows-esp32", "windows", "esp32", false)]
    public void Recomendacao_por_cenario(string scenario, string situation, string recommended, bool wifiAvailable)
    {
        var advice = Advise(scenario);
        Assert.Equal(situation, advice.Situation);
        Assert.Equal(recommended, Assert.Single(advice.Options, o => o.Recommended).Engine);
        Assert.Equal(wifiAvailable, advice.Options.Single(o => o.Engine == "wifi").Available);
        Assert.True(advice.Options.Single(o => o.Engine == "esp32").Available);
    }

    [Fact]
    public void Com_duas_placas_escolhe_a_compativel_que_nao_e_a_internet()
    {
        var advice = Advise("linux-compatible");
        Assert.Equal("phy1", advice.BestCard!.Card.Phy); // ALFA, não a AX200 da internet
        Assert.Empty(advice.SuggestedUsbAdapters);
    }

    [Fact]
    public void Placa_ruim_sugere_adaptador_usb_do_catalogo()
    {
        var advice = Advise("linux-problematic");
        Assert.Contains(advice.SuggestedUsbAdapters, a => a.Id == "alfa-awus036achm");
        Assert.All(advice.SuggestedUsbAdapters, a => Assert.Equal("usb", a.Type));
        Assert.Equal("problematic", advice.Options.Single(o => o.Engine == "wifi").ReasonCode);
    }
}

public sealed class ProdKeysTests
{
    private const string Hex16 = "00112233445566778899aabbccddeeff";

    private static byte[] File(string text) => Encoding.UTF8.GetBytes(text);

    private static string Valid(params string[] extra) => string.Join('\n', new[]
    {
        $"aes_kek_generation_source = {Hex16}",
        $"aes_key_generation_source = {Hex16}",
        $"master_key_00 = {Hex16}",
        $"master_key_12 = {Hex16}",
        $"header_key = {Hex16}{Hex16}",
    }.Concat(extra)) + "\n";

    [Fact]
    public void Arquivo_valido()
    {
        var v = ProdKeys.Validate(File(Valid()));
        Assert.True(v.Valid);
        Assert.Equal([1, 3], v.Protocols);
        Assert.Empty(v.MissingRequired);
        Assert.Equal(5, v.KeyCount);
    }

    [Fact]
    public void Uma_master_key_basta()
    {
        var text = Valid().Replace($"master_key_12 = {Hex16}\n", "");
        var v = ProdKeys.Validate(File(text));
        Assert.True(v.Valid);
        Assert.Equal([1], v.Protocols);
    }

    [Fact]
    public void Faltando_chaves_obrigatorias()
    {
        var v = ProdKeys.Validate(File($"master_key_00 = {Hex16}\n"));
        Assert.False(v.Valid);
        Assert.Contains("aes_kek_generation_source", v.MissingRequired);
        Assert.Contains("aes_key_generation_source", v.MissingRequired);
    }

    [Fact]
    public void Linha_fora_do_formato_quebraria_o_loader_do_motor()
    {
        var v = ProdKeys.Validate(File(Valid("# comentário", "sem_valor", "chave = xyz")));
        Assert.False(v.Valid);
        Assert.Equal(3, v.InvalidLines.Count);
    }

    [Fact]
    public void Chave_obrigatoria_com_tamanho_errado_nao_conta()
    {
        var v = ProdKeys.Validate(File(Valid().Replace($"master_key_00 = {Hex16}", "master_key_00 = 0011").Replace($"master_key_12 = {Hex16}\n", "")));
        Assert.False(v.Valid);
        Assert.Contains("master_key_00", v.MissingRequired);
    }

    [Fact]
    public void Resultado_nunca_contem_valores()
    {
        var json = JsonSerializer.Serialize(ProdKeys.Validate(File(Valid())));
        Assert.DoesNotContain(Hex16, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guarda_com_permissao_600_e_nao_guarda_invalido()
    {
        if (OperatingSystem.IsWindows())
            return;
        var dir = Directory.CreateTempSubdirectory("traderock-keys-").FullName;
        try
        {
            var keys = new ProdKeys(new AppPaths(dir));
            Assert.False(keys.Save(File("lixo")).Valid);
            Assert.Null(keys.Current());

            Assert.True(keys.Save(File(Valid())).Valid);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, System.IO.File.GetUnixFileMode(keys.FilePath));
            Assert.True(keys.Current()!.Valid);

            keys.Delete();
            Assert.Null(keys.Current());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public sealed class SetupChecksTests
{
    private static readonly KeysValidation GoodKeys = new(true, [], ["aes_kek_generation_source"], [], [1], 3);

    private static IReadOnlyList<SetupCheck> Checks(string scenario, SetupState state, KeysValidation? keys = null)
    {
        var system = SetupScenarios.All[scenario];
        var catalog = HardwareCatalog.Embedded;
        return SetupChecks.Evaluate(system, RadioAdvisor.Advise(system, catalog), state, keys, catalog);
    }

    private static SetupCheck Get(IReadOnlyList<SetupCheck> checks, string id) => checks.Single(c => c.Id == id);

    [Fact]
    public void Sem_nada_configurado()
    {
        var checks = Checks("linux-compatible", new SetupState());
        Assert.Equal("keys_missing", Get(checks, "keys").Code);
        Assert.Equal("radio_not_chosen", Get(checks, "radio").Code);
        Assert.Equal(CheckStatus.Pending, Get(checks, "switch_test").Status);
    }

    [Fact]
    public void Pular_as_chaves_vira_aviso_mas_continua_bloqueando()
    {
        var keys = Get(Checks("linux-compatible", new SetupState { KeysSkipped = true }), "keys");
        Assert.Equal(CheckStatus.Warning, keys.Status);
        Assert.True(keys.Essential);
    }

    [Fact]
    public void Placa_compativel_escolhida()
    {
        var checks = Checks("linux-compatible", new SetupState { Radio = "wifi", Phy = "phy1" }, GoodKeys);
        Assert.Equal(CheckStatus.Ok, Get(checks, "native_linux").Status);
        Assert.Equal("phy_recommended", Get(checks, "phy").Code);
        Assert.DoesNotContain(checks, c => c.Id == "phy_internet");
        Assert.Equal("card_test_not_run", Get(checks, "card_test").Code);
        Assert.Equal("worker_installed", Get(checks, "worker").Code);
        Assert.Equal("network_manager_unconfigured", Get(checks, "network_manager").Code);
        Assert.DoesNotContain(checks, c => c.Id == "tools");
    }

    [Fact]
    public void Teste_da_placa_vale_so_para_a_mesma_placa()
    {
        var passed = new CardTestResult("phy1", true, "passed", default, [], Driver: "mt76x0u");
        SetupState State(CardTestResult test) => new() { Radio = "wifi", Phy = "phy1", CardTest = test };
        Assert.Equal("card_test_passed", Get(Checks("linux-compatible", State(passed), GoodKeys), "card_test").Code);
        // Outro adaptador USB ligado depois pode virar o mesmo phy1.
        Assert.Equal("card_test_not_run", Get(Checks("linux-compatible", State(passed with { Driver = "rtl8xxxu" }), GoodKeys), "card_test").Code);
        var noRx = passed with { Code = "passed_no_rx" };
        var checks = Checks("linux-compatible", State(noRx), GoodKeys);
        Assert.Equal(CheckStatus.Ok, Get(checks, "card_test").Status);
        Assert.False(Get(checks, "card_test_note").Essential);
    }

    [Fact]
    public void Placa_que_e_a_internet_do_pc_avisa()
    {
        var checks = Checks("linux-unstable", new SetupState { Radio = "wifi", Phy = "phy0" }, GoodKeys);
        Assert.Equal("phy_unstable", Get(checks, "phy").Code);
        Assert.Equal(CheckStatus.Warning, Get(checks, "phy_internet").Status);
    }

    [Fact]
    public void Wifi_escolhido_no_wsl_explica_o_motivo()
    {
        var checks = Checks("wsl", new SetupState { Radio = "wifi" }, GoodKeys);
        Assert.Equal("radio_unavailable", Get(checks, "radio").Code);
        Assert.Equal("not_native_wsl", Get(checks, "native_linux").Code);
    }

    [Fact]
    public void Esp32_no_linux_grupo_brltty_e_udev()
    {
        var checks = Checks("linux-esp32", new SetupState { Radio = "esp32", Esp32Board = "esp32-s3", SerialPort = "/dev/ttyUSB0" }, GoodKeys);

        Assert.Equal("esp32_board_recommended", Get(checks, "esp32_board").Code);
        Assert.Equal("WCH CH340", Get(checks, "serial_port").Args!["chip"]);
        var group = Get(checks, "serial_group");
        Assert.Equal(CheckStatus.Error, group.Status);
        Assert.Equal("sudo usermod -aG uucp $USER", group.Fix);
        Assert.Equal(CheckStatus.Warning, Get(checks, "brltty").Status);
        Assert.Contains("ATTRS{idVendor}==\"1a86\"", Get(checks, "udev_rule").Fix);
        Assert.Equal("entrega-9", Get(checks, "esp32_firmware").AvailableIn);
    }

    [Fact]
    public void No_grupo_mas_sem_sair_e_entrar()
    {
        var system = SetupScenarios.All["linux-esp32"] with { User = new UserInfo("ash", ["ash", "uucp"], ["ash"]) };
        var catalog = HardwareCatalog.Embedded;
        var checks = SetupChecks.Evaluate(system, RadioAdvisor.Advise(system, catalog),
            new SetupState { Radio = "esp32", Esp32Board = "esp32-s3", SerialPort = "/dev/ttyUSB0" }, GoodKeys, catalog);
        Assert.Equal("serial_group_relogin", Get(checks, "serial_group").Code);
    }

    [Fact]
    public void Windows_sem_driver_do_ch340_aponta_o_site_do_fabricante()
    {
        var checks = Checks("windows-esp32", new SetupState { Radio = "esp32", Esp32Board = "esp32-s3", SerialPort = "COM3" }, GoodKeys);
        var driver = Get(checks, "usb_driver");
        Assert.Equal(CheckStatus.Error, driver.Status);
        Assert.Equal("https://www.wch-ic.com/downloads/CH341SER_EXE.html", driver.Args!["url"]);
        Assert.DoesNotContain(checks, c => c.Id == "serial_group"); // grupos são coisa do Linux
    }

    [Fact]
    public void Esp32_classico_exige_confirmar_o_modulo_e_o_antigo_nao_e_suportado()
    {
        var state = new SetupState { Radio = "esp32", Esp32Board = "esp32-wroom-32e", SerialPort = "/dev/ttyUSB0" };
        Assert.Equal("esp32_confirm_module", Get(Checks("linux-esp32", state, GoodKeys), "esp32_board").Code);
        Assert.Equal(CheckStatus.Warning, Get(Checks("linux-esp32", state with { Esp32ModuleConfirmed = true }, GoodKeys), "esp32_board").Status);
        Assert.Equal("esp32_board_unsupported", Get(Checks("linux-esp32", state with { Esp32Board = "esp32-wroom-32" }, GoodKeys), "esp32_board").Code);
    }

    [Fact]
    public void Esp32_c3_numa_porta_de_conversor_avisa()
    {
        var checks = Checks("linux-esp32", new SetupState { Radio = "esp32", Esp32Board = "esp32-c3", SerialPort = "/dev/ttyUSB0" }, GoodKeys);
        Assert.Equal("port_should_be_native", Get(checks, "usb_port_kind").Code);
    }

    [Fact]
    public void Troca_real_fica_bloqueada_enquanto_ha_pendencias()
    {
        var system = SetupScenarios.All["linux-compatible"];
        var catalog = HardwareCatalog.Embedded;
        var advice = RadioAdvisor.Advise(system, catalog);
        var state = new SetupState { Radio = "wifi", Phy = "phy1", SwitchTestPassed = true };
        var status = new SetupStatus(system, advice, state, GoodKeys, SetupChecks.Evaluate(system, advice, state, GoodKeys, catalog));
        Assert.False(status.ReadyToTrade); // worker e teste da placa chegam na Entrega 6
    }

    [Fact]
    public void Relatorio_sem_dados_sensiveis()
    {
        var system = SetupScenarios.All["linux-esp32"];
        var catalog = HardwareCatalog.Embedded;
        var advice = RadioAdvisor.Advise(system, catalog);
        var state = new SetupState { Radio = "esp32", Esp32Board = "esp32-s3", SerialPort = "/dev/ttyUSB0" };
        var report = SetupReport.Build(new SetupStatus(system, advice, state, GoodKeys, SetupChecks.Evaluate(system, advice, state, GoodKeys, catalog)), "0.1.0");

        Assert.Contains("iwlwifi", report);
        Assert.Contains("1a86:7523", report);
        Assert.Contains("formato válido", report);
        Assert.DoesNotContain("ash", report); // nome do usuário
        Assert.DoesNotContain("aes_kek", report); // nem nomes de chaves
    }
}
