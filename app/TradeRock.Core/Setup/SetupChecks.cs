using System.Text;
using TradeRock.Core.Esp32;

namespace TradeRock.Core.Setup;

/// <summary>O que a pessoa escolheu/confirmou no assistente (guardado nas configurações).</summary>
public sealed record SetupState
{
    public bool Completed { get; init; }
    public string? Radio { get; init; } // wifi | esp32
    public string? Phy { get; init; }
    public string? SerialPort { get; init; }

    /// <summary>Id da placa ESP32 no catálogo (ex.: esp32-s3), informada pela pessoa até a detecção por esptool (Entrega 9).</summary>
    public string? Esp32Board { get; init; }

    public bool KeysSkipped { get; init; }

    /// <summary>Último teste real da placa (modo monitor + quadros de ação + modo AP).</summary>
    public CardTestResult? CardTest { get; init; }

    /// <summary>Último teste com o Switch (beacon da sala encontrado e decifrado).</summary>
    public SwitchTestResult? SwitchTest { get; init; }

    /// <summary>ESP32: última resposta do firmware (LDN_HELLO) na porta escolhida.</summary>
    public Esp32ProbeResult? Esp32Probe { get; init; }

    /// <summary>ESP32: chip lido pelo esptool (tipo, revisão, flash).</summary>
    public Esp32ChipResult? Esp32Chip { get; init; }

    /// <summary>ESP32: última conferência (ou gravação) do firmware embutido.</summary>
    public Esp32FirmwareResult? Esp32Firmware { get; init; }
}

/// <summary>
/// LDN_HELLO + LDN_BEGIN + LDN_STOP na porta. <see cref="Code"/>: hello_ok, esp32_firmware_missing,
/// esp32_firmware_incompatible, esp32_no_response, serial_permission, serial_busy, serial_port_missing, esp32_error.
/// <see cref="Target"/> é o chip que o firmware informa (esp32, esp32c3, esp32c6, esp32s3).
/// </summary>
public sealed record Esp32ProbeResult(string Port, bool Ok, string Code, string? Target, DateTimeOffset At, bool Simulated = false);

/// <summary>Chip lido pelo esptool. <see cref="Revision"/> = maior × 100 + menor (v3.1 → 301).</summary>
public sealed record Esp32ChipResult(string Port, string Target, string Description, int Revision, string? FlashSize, DateTimeOffset At, bool Simulated = false);

/// <summary>Firmware embutido comparado com a flash (verify-flash), depois de gravar ou a pedido.</summary>
public sealed record Esp32FirmwareResult(string Port, string Target, bool Matches, bool Written, DateTimeOffset At, bool Simulated = false);

public sealed record CardTestChannel(int Channel, bool Set, int Sent, int Received, string? Error);

/// <summary>
/// Resultado do teste da placa (worker/traderock_worker/card_child.py). <see cref="Code"/> é estável.
/// <see cref="Ap"/>: a placa abriu uma rede em modo AP com o monitor ao lado, o que o pokeldn faz para abrir a sala
/// dos jogos novos (D23). Sem isso ela só troca com o FRLG; null = teste de antes da Entrega 17.
/// </summary>
public sealed record CardTestResult(
    string Phy,
    bool Passed,
    string Code,
    DateTimeOffset At,
    IReadOnlyList<CardTestChannel> Channels,
    string? Error = null,
    bool Simulated = false,
    string? Driver = null, // módulo/driver da placa testada: o mesmo phyN pode ser outra placa depois
    bool? Ap = null,
    string? ApError = null)
{
    public bool IsFor(WifiCard card) => Phy == card.Phy && (Driver is null || Driver == (card.Module ?? card.Driver));
}

public sealed record SwitchTestNetwork(string CommId, int Channel, int Protocol, bool Frlg, int Participants, int MaxParticipants);

public sealed record SwitchTestChannel(int Channel, bool Usable, string? Error);

/// <summary>
/// Resultado do teste com o Switch (worker/traderock_worker/beacon_child.py). <see cref="Code"/> é estável:
/// found_decrypted, found_not_decrypted, other_session, not_found, monitor_failed, channel_failed,
/// scan_failed, engine_error, cancelled.
/// </summary>
public sealed record SwitchTestResult(
    string Phy,
    bool Passed,
    string Code,
    DateTimeOffset At,
    IReadOnlyList<SwitchTestNetwork> Networks,
    IReadOnlyList<SwitchTestChannel> Channels,
    int Undecryptable = 0,
    IReadOnlyList<int>? UndecryptableChannels = null,
    int Sweeps = 0,
    double Seconds = 0,
    string? Error = null,
    bool Simulated = false,
    string? Driver = null,
    DateTimeOffset? KeysSavedAt = null, // o prod.keys testado (data de gravação, nunca o conteúdo)
    string Radio = "wifi") // com o ESP32, Phy é a porta serial e Driver é o chip do firmware
{
    /// <summary>Vale para esta placa e este prod.keys: trocar qualquer um dos dois pede um teste novo.</summary>
    public bool IsFor(WifiCard card, KeysValidation? keys) =>
        Radio == "wifi" && Phy == card.Phy && (Driver is null || Driver == (card.Module ?? card.Driver))
        && keys is not null && KeysSavedAt == keys.SavedAt;

    /// <summary>ESP32: vale para esta porta, este chip e este prod.keys.</summary>
    public bool IsForEsp32(string port, string? target, KeysValidation? keys) =>
        Radio == "esp32" && Phy == port && Driver == target && keys is not null && KeysSavedAt == keys.SavedAt;
}

public enum CheckStatus
{
    Ok,
    Warning,
    Error,
    Pending, // depende de algo que ainda vai chegar (worker, firmware, teste com o Switch)
    Info,
}

/// <summary>
/// Uma verificação do assistente. <see cref="Code"/> e <see cref="Args"/> viram texto no frontend;
/// <see cref="Fix"/> é um comando ou ação sugerida (o app nunca roda sudo por conta própria).
/// </summary>
public sealed record SetupCheck(
    string Id,
    int Step,
    CheckStatus Status,
    bool Essential,
    string Code,
    IReadOnlyDictionary<string, string>? Args = null,
    string? Fix = null,
    string? AvailableIn = null); // "entrega-6" etc. quando Pending por falta de funcionalidade

public sealed record SetupStatus(
    SystemSnapshot System,
    RadioAdvice Advice,
    SetupState State,
    KeysValidation? Keys,
    IReadOnlyList<SetupCheck> Checks)
{
    /// <summary>Tudo o que é essencial passou: a troca com um motor real pode ser liberada.</summary>
    public bool ReadyToTrade => Checks.Where(c => c.Essential).All(c => c.Status == CheckStatus.Ok);
}

public static class SetupChecks
{
    public static IReadOnlyList<SetupCheck> Evaluate(SystemSnapshot system, RadioAdvice advice, SetupState state,
        KeysValidation? keys, HardwareCatalog catalog)
    {
        var checks = new List<SetupCheck>();

        // Etapa 2: prod.keys
        checks.Add(keys switch
        {
            { Valid: true } => new SetupCheck("keys", 2, CheckStatus.Ok, true, "keys_ok",
                Args(("protocols", string.Join(", ", keys.Protocols)))),
            not null => new SetupCheck("keys", 2, CheckStatus.Error, true, "keys_invalid",
                Args(("missing", string.Join(", ", keys.MissingRequired)), ("lines", string.Join(", ", keys.InvalidLines)))),
            null when state.KeysSkipped => new SetupCheck("keys", 2, CheckStatus.Warning, true, "keys_skipped"),
            _ => new SetupCheck("keys", 2, CheckStatus.Error, true, "keys_missing"),
        });

        // Etapa 3: rádio escolhido (e disponível neste sistema)
        var chosen = advice.Options.FirstOrDefault(o => o.Engine == state.Radio);
        checks.Add(chosen switch
        {
            null => new SetupCheck("radio", 3, CheckStatus.Error, true, "radio_not_chosen"),
            { Available: false } => new SetupCheck("radio", 3, CheckStatus.Error, true, "radio_unavailable", Args(("reason", chosen.ReasonCode ?? ""))),
            _ => new SetupCheck("radio", 3, CheckStatus.Ok, true, $"radio_{chosen.Engine}"),
        });

        // Etapa 4: preparar o rádio escolhido
        if (state.Radio == RadioAdvisor.Wifi)
            checks.AddRange(WifiChecks(system, advice, state));
        else if (state.Radio == RadioAdvisor.Esp32)
            checks.AddRange(Esp32Checks(system, state, catalog));

        // Etapa 6: teste com o Switch (beacon da sala encontrado e decifrado, sem entrar na sala).
        checks.Add(SwitchTestCheck(advice, state, keys));

        return checks;
    }

    private static IEnumerable<SetupCheck> WifiChecks(SystemSnapshot system, RadioAdvice advice, SetupState state)
    {
        if (system.Os != "linux" || system.IsWsl || system.VirtualMachine is not null)
        {
            var reason = system.Os != "linux" ? "windows" : system.IsWsl ? "wsl" : "vm";
            yield return new SetupCheck("native_linux", 4, CheckStatus.Error, true, $"not_native_{reason}",
                Args(("vm", system.VirtualMachine ?? "")));
            yield break;
        }
        yield return new SetupCheck("native_linux", 4, CheckStatus.Ok, true, "native_linux_ok");

        yield return WorkerCheck(system);

        if (system.MissingTools is { Count: > 0 } missing)
            yield return new SetupCheck("tools", 4, CheckStatus.Error, true, "tools_missing",
                Args(("tools", string.Join(", ", missing))), Fix: InstallCommand(system.Distro, missing.Select(ToolPackage)));

        var card = advice.Cards.FirstOrDefault(c => c.Card.Phy == state.Phy);
        if (advice.Cards.Count == 0)
        {
            yield return new SetupCheck("phy", 4, CheckStatus.Error, true, "no_wifi_card");
            yield break;
        }
        if (card is null)
        {
            yield return new SetupCheck("phy", 4, CheckStatus.Error, true, "phy_not_chosen");
            yield break;
        }

        var cardArgs = Args(("phy", card.Card.Phy), ("driver", card.Card.Module ?? card.Card.Driver ?? "?"),
            ("model", card.Matches.FirstOrDefault()?.Model ?? card.Card.ProductName ?? ""));
        yield return card.Status switch
        {
            CompatStatus.Recommended or CompatStatus.Works => new SetupCheck("phy", 4, CheckStatus.Ok, true, $"phy_{Name(card.Status)}", cardArgs),
            CompatStatus.Untested or CompatStatus.Unstable => new SetupCheck("phy", 4, CheckStatus.Warning, false, $"phy_{Name(card.Status)}", cardArgs),
            _ => new SetupCheck("phy", 4, CheckStatus.Error, false, $"phy_{Name(card.Status)}", cardArgs),
        };

        if (card.Card.IsInternetConnection)
            yield return new SetupCheck("phy_internet", 4, CheckStatus.Warning, false, "phy_is_internet",
                Args(("iface", string.Join(", ", card.Card.Interfaces))));

        // O teste real (modo monitor + quadros de ação) é o que diz se a placa serve.
        var test = state.CardTest is { } t && t.IsFor(card.Card) ? t : null;
        yield return test switch
        {
            null => new SetupCheck("card_test", 4, CheckStatus.Error, true, "card_test_not_run", cardArgs),
            { Passed: true } => new SetupCheck("card_test", 4, CheckStatus.Ok, true, "card_test_passed", cardArgs),
            _ => new SetupCheck("card_test", 4, CheckStatus.Error, true, "card_test_failed",
                new Dictionary<string, string>(cardArgs) { ["code"] = test.Code }),
        };
        // Passou, mas com ressalva (nada recebido, ou só alguns canais): avisa sem bloquear.
        if (test is { Passed: true, Code: not "passed" })
            yield return new SetupCheck("card_test_note", 4, CheckStatus.Warning, false, $"card_test_{test.Code}", cardArgs);
        // Sem modo AP a placa não abre a sala dos jogos novos: o FRLG continua trocando.
        if (test is { Passed: true })
        {
            if (test.Ap == false)
                yield return new SetupCheck("card_test_ap", 4, CheckStatus.Warning, false, "card_test_no_ap", cardArgs);
            else if (test.Ap is null)
                yield return new SetupCheck("card_test_ap", 4, CheckStatus.Info, false, "card_test_ap_unknown", cardArgs);
        }

        if (system.NetworkManagerRunning)
            yield return system.LdnNetwork is { Complete: true }
                ? new SetupCheck("network_manager", 4, CheckStatus.Ok, false, "network_manager_ok")
                : new SetupCheck("network_manager", 4, CheckStatus.Warning, false, "network_manager_unconfigured");
    }

    private static SetupCheck SwitchTestCheck(RadioAdvice advice, SetupState state, KeysValidation? keys)
    {
        SwitchTestResult? test;
        if (state.Radio == RadioAdvisor.Esp32)
        {
            // O teste usa o firmware: primeiro ele precisa responder na porta escolhida.
            var probe = state.Esp32Probe is { Ok: true } p && p.Port == state.SerialPort ? p : null;
            if (probe is null)
                return new SetupCheck("switch_test", 6, CheckStatus.Pending, true, "switch_test_needs_radio");
            if (keys is not { Valid: true })
                return new SetupCheck("switch_test", 6, CheckStatus.Pending, true, "switch_test_needs_keys");
            test = state.SwitchTest is { } e && e.IsForEsp32(probe.Port, probe.Target, keys) ? e : null;
        }
        else
        {
            var card = advice.Cards.FirstOrDefault(c => c.Card.Phy == state.Phy)?.Card;
            if (state.Radio != RadioAdvisor.Wifi || card is null)
                return new SetupCheck("switch_test", 6, CheckStatus.Pending, true, "switch_test_needs_radio");
            if (keys is not { Valid: true })
                return new SetupCheck("switch_test", 6, CheckStatus.Pending, true, "switch_test_needs_keys");
            test = state.SwitchTest is { } t && t.IsFor(card, keys) ? t : null;
        }
        if (test is null)
            return new SetupCheck("switch_test", 6, CheckStatus.Error, true, "switch_test_not_run");
        if (test.Passed)
            return new SetupCheck("switch_test", 6, CheckStatus.Ok, true, "switch_test_ok",
                Args(("channel", test.Networks.FirstOrDefault(n => n.Frlg)?.Channel.ToString() ?? "?")));
        var args = new Dictionary<string, string> { ["code"] = test.Code };
        // Anúncio que não decifra com um prod.keys só com a master_key_00: a sala pode ser do protocolo 3.
        if (test.Code == "found_not_decrypted" && !keys.Protocols.Contains(3))
            args["hint"] = "missing_master_key_12";
        return new SetupCheck("switch_test", 6, CheckStatus.Error, true, "switch_test_failed", args);
    }

    private static SetupCheck WorkerCheck(SystemSnapshot system) => system switch
    {
        // AppImage: o worker vem embutido e é instalado (ou atualizado) pelo próprio app, com confirmação.
        { BundledWorker: { Available: true, Installed: false }, Worker: null or { Flavor: "appimage" } } =>
            new SetupCheck("worker", 4, CheckStatus.Error, true, "worker_appimage_missing"),
        { BundledWorker: { Available: true, UpToDate: false }, Worker: { Flavor: "appimage" } } =>
            new SetupCheck("worker", 4, CheckStatus.Error, true, "worker_appimage_outdated"),
        _ => InstalledWorkerCheck(system),
    };

    private static SetupCheck InstalledWorkerCheck(SystemSnapshot system) => system.Worker switch
    {
        null => new SetupCheck("worker", 4, CheckStatus.Error, true, "worker_missing"),
        { PkexecFound: false } => new SetupCheck("worker", 4, CheckStatus.Error, true, "pkexec_missing",
            Fix: InstallCommand(system.Distro, [IsFamily(system.Distro, "debian") ? "pkexec" : "polkit"])),
        { Safe: false } w => new SetupCheck("worker", 4, CheckStatus.Error, true, "worker_unsafe", Args(("path", w.Launcher))),
        { PolicyInstalled: false } => new SetupCheck("worker", 4, CheckStatus.Error, true, "worker_policy_missing"),
        { } w => new SetupCheck("worker", 4, CheckStatus.Ok, true, "worker_installed", Args(("flavor", w.Flavor))),
    };

    /// <summary>Pacote que traz cada ferramenta (o "ip" vem do iproute2).</summary>
    private static string ToolPackage(string tool) => tool switch { "ip" => "iproute2", "sysctl" => "procps", _ => tool };

    private static bool IsFamily(DistroInfo? d, string family) =>
        d is not null && (d.Id == family || d.IdLike.Split(' ').Contains(family));

    /// <summary>Comando de instalação para a família da distribuição (null se não souber).</summary>
    public static string? InstallCommand(DistroInfo? d, IEnumerable<string> packages)
    {
        var list = string.Join(' ', packages);
        if (IsFamily(d, "debian") || IsFamily(d, "ubuntu"))
            return $"sudo apt install {list}";
        if (IsFamily(d, "arch"))
            return $"sudo pacman -S {list}";
        if (IsFamily(d, "fedora") || IsFamily(d, "rhel"))
            return $"sudo dnf install {list.Replace("iproute2", "iproute")}";
        if (IsFamily(d, "suse") || IsFamily(d, "opensuse"))
            return $"sudo zypper install {list}";
        return null;
    }

    private static IEnumerable<SetupCheck> Esp32Checks(SystemSnapshot system, SetupState state, HardwareCatalog catalog)
    {
        // Placa informada pela pessoa; o chip lido pelo esptool e pelo LDN_HELLO é conferido abaixo.
        var board = catalog.Esp32.FirstOrDefault(b => b.Id == state.Esp32Board);
        if (board is null)
            yield return new SetupCheck("esp32_board", 4, CheckStatus.Error, true, "esp32_board_not_chosen");
        else if (board.Status == CompatStatus.Unsupported)
            yield return new SetupCheck("esp32_board", 4, CheckStatus.Error, true, "esp32_board_unsupported", Args(("model", board.Model)));
        else
            yield return new SetupCheck("esp32_board", 4, board.Status == CompatStatus.Untested ? CheckStatus.Warning : CheckStatus.Ok,
                board.Status != CompatStatus.Untested, $"esp32_board_{Name(board.Status)}", Args(("model", board.Model)));

        // Windows: conversor ligado sem driver não ganha porta COM. Avisa com o link do fabricante.
        // (Só os conversores do catálogo: outro aparelho USB sem driver não é problema do TradeRock.)
        foreach (var missing in system.SerialPorts.Where(p => p.DriverMissing).DistinctBy(p => (p.VendorId, p.ProductId)))
        {
            if (catalog.MatchUsbSerial(missing.VendorId, missing.ProductId) is not { } missingChip)
                continue;
            yield return new SetupCheck("usb_driver", 4, CheckStatus.Error, true, "usb_driver_missing",
                Args(("chip", missingChip.Name), ("url", missingChip.DriverUrl ?? "")));
        }

        var ports = system.SerialPorts.Where(p => !p.DriverMissing).ToList();
        var port = ports.FirstOrDefault(p => p.Path == state.SerialPort);
        if (ports.Count == 0)
            yield return new SetupCheck("serial_port", 4, CheckStatus.Error, true, "serial_none");
        else if (port is null)
            yield return new SetupCheck("serial_port", 4, CheckStatus.Error, true, "serial_not_chosen", Args(("count", ports.Count.ToString())));
        else
        {
            var chip = catalog.MatchUsbSerial(port.VendorId, port.ProductId);
            yield return new SetupCheck("serial_port", 4, CheckStatus.Ok, true, "serial_ok",
                Args(("port", port.Path), ("chip", chip?.Name ?? port.ProductName ?? "?")));

            // Conferência da porta com o tipo de placa: S3, C3 e C6 usam a USB nativa; o clássico, o conversor.
            if (board is not null && chip is not null)
            {
                var native = chip.Vid == "303a";
                if (board.Connection == "native_usb" && !native)
                    yield return new SetupCheck("usb_port_kind", 4, CheckStatus.Warning, false, "port_should_be_native", Args(("model", board.Model)));
                if (board.Connection == "uart" && native)
                    yield return new SetupCheck("usb_port_kind", 4, CheckStatus.Warning, false, "port_should_be_uart", Args(("model", board.Model)));
            }
        }

        if (system.Os == "linux" && !system.IsWsl)
        {
            var group = system.SerialGroup;
            var user = system.User;
            if (user is null || !user.Groups.Contains(group))
                yield return new SetupCheck("serial_group", 4, CheckStatus.Error, true, "serial_group_missing",
                    Args(("group", group)), Fix: $"sudo usermod -aG {group} $USER");
            else if (!user.SessionGroups.Contains(group))
                yield return new SetupCheck("serial_group", 4, CheckStatus.Warning, true, "serial_group_relogin", Args(("group", group)));
            else
                yield return new SetupCheck("serial_group", 4, CheckStatus.Ok, true, "serial_group_ok", Args(("group", group)));

            if (system.BrlttyRunning)
                yield return new SetupCheck("brltty", 4, CheckStatus.Warning, false, "brltty_running",
                    Fix: "sudo systemctl stop brltty-udev.service brltty.service && sudo systemctl mask brltty-udev.service brltty.service");

            if (port is { VendorId: { } vid, ProductId: { } pid })
                yield return new SetupCheck("udev_rule", 4, CheckStatus.Info, false, "udev_rule_optional",
                    Args(("path", "/dev/frlg-esp32")),
                    Fix: $"echo 'SUBSYSTEM==\"tty\", ATTRS{{idVendor}}==\"{vid}\", ATTRS{{idProduct}}==\"{pid}\", SYMLINK+=\"frlg-esp32\", GROUP=\"{group}\", MODE=\"0660\"' | sudo tee /etc/udev/rules.d/99-frlg-esp32.rules && sudo udevadm control --reload-rules && sudo udevadm trigger");
        }

        foreach (var check in Esp32FirmwareChecks(state, board, port?.Path))
            yield return check;
    }

    /// <summary>
    /// Firmware e chip. O LDN_HELLO (rápido, sem reiniciar a placa) diz se há firmware compatível e de qual
    /// chip; o esptool (sob pedido) diz o chip, a revisão e a flash, e se o firmware é o que o app traz.
    /// </summary>
    private static IEnumerable<SetupCheck> Esp32FirmwareChecks(SetupState state, Esp32Entry? board, string? port)
    {
        if (port is null)
        {
            yield return new SetupCheck("esp32_firmware", 4, CheckStatus.Pending, true, "esp32_firmware_needs_port");
            yield break;
        }

        var chip = state.Esp32Chip is { } c && c.Port == port ? c : null;
        if (chip is not null)
        {
            var image = Esp32Firmware.For(chip.Target);
            if (image is null)
                yield return new SetupCheck("esp32_chip", 4, CheckStatus.Error, true, "esp32_chip_unsupported", Args(("chip", chip.Description)));
            else if (board is not null && board.Chip != chip.Target)
                yield return new SetupCheck("esp32_chip", 4, CheckStatus.Error, true, "esp32_board_mismatch",
                    Args(("model", board.Model), ("chip", chip.Description)));
            else
                yield return new SetupCheck("esp32_chip", 4, CheckStatus.Ok, false, "esp32_chip_ok", Args(("chip", chip.Description), ("flash", chip.FlashSize ?? "?")));

            if (image is not null && FlashMb(chip.FlashSize) is { } mb && mb < image.FlashSizeMb)
                yield return new SetupCheck("esp32_flash_size", 4, CheckStatus.Error, true, "esp32_flash_too_small",
                    Args(("size", chip.FlashSize!), ("needed", image.FlashSize)));

            // ESP32 clássico: o WROOM-32E usa o chip D0WD-V3 (revisão 3); revisão menor indica o WROOM-32 antigo, que em
            // algumas placas não ouve o Switch (docs/hardware_esp32.md do pokeldn). O teste com o Switch tira a dúvida.
            if (chip.Target == "esp32" && chip.Revision < 300)
                yield return new SetupCheck("esp32_revision", 4, CheckStatus.Warning, false, "esp32_old_revision", Args(("chip", chip.Description)));
        }

        var probe = state.Esp32Probe is { } p && p.Port == port ? p : null;
        yield return probe switch
        {
            null => new SetupCheck("esp32_firmware", 4, CheckStatus.Error, true, "esp32_firmware_not_checked"),
            // Sem leitura do esptool, o LDN_HELLO é quem denuncia a placa marcada errada (com a leitura, o
            // item do chip acima já avisa).
            { Ok: true } when board is not null && probe.Target != board.Chip && chip is null =>
                new SetupCheck("esp32_firmware", 4, CheckStatus.Error, true, "esp32_firmware_other_chip",
                    Args(("model", board.Model), ("target", Esp32Firmware.DisplayName(probe.Target)))),
            { Ok: true } => new SetupCheck("esp32_firmware", 4, CheckStatus.Ok, true, "esp32_firmware_ok", Args(("target", Esp32Firmware.DisplayName(probe.Target)))),
            _ => new SetupCheck("esp32_firmware", 4, CheckStatus.Error, true, $"esp32_probe_{probe.Code}"),
        };

        if (state.Esp32Firmware is { } fw && fw.Port == port)
            yield return fw.Matches
                ? new SetupCheck("esp32_firmware_version", 4, CheckStatus.Ok, false, "esp32_firmware_verified")
                : new SetupCheck("esp32_firmware_version", 4, CheckStatus.Warning, false, "esp32_firmware_other_version");
    }

    private static int? FlashMb(string? size) =>
        size is not null && size.EndsWith("MB", StringComparison.Ordinal) && int.TryParse(size[..^2], out var mb) ? mb : null;

    private static string Name(CompatStatus status) => status.ToString().ToLowerInvariant();

    private static Dictionary<string, string> Args(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value);
}

/// <summary>
/// Relatório para pedir ajuda em fóruns. Sem prod.keys (nem nomes das chaves), sem MACs, sem o nome
/// do usuário e sem caminhos pessoais.
/// </summary>
public static class SetupReport
{
    public static string Build(SetupStatus status, string appVersion)
    {
        var s = status.System;
        var sb = new StringBuilder();
        sb.AppendLine("## Relatório do TradeRock");
        sb.AppendLine();
        sb.AppendLine($"- Versão: {appVersion}");
        sb.AppendLine($"- Sistema: {s.Os}{(s.Distro is { } d ? $" ({d.Name})" : "")}");
        sb.AppendLine($"- WSL: {(s.IsWsl ? "sim" : "não")} · Máquina virtual: {s.VirtualMachine ?? "não"}");
        sb.AppendLine($"- NetworkManager: {(s.NetworkManagerRunning ? "ativo" : "inativo")} · brltty: {(s.BrlttyRunning ? "ativo" : "inativo")}");
        if (s.SimulatedScenario is { } scenario)
            sb.AppendLine($"- (cenário simulado: {scenario})");
        sb.AppendLine();
        sb.AppendLine("### Placas Wi-Fi");
        if (status.Advice.Cards.Count == 0)
            sb.AppendLine("- nenhuma");
        foreach (var c in status.Advice.Cards)
        {
            var ids = c.Card.VendorId is null ? "" : $" [{c.Card.VendorId}:{c.Card.ProductId}]";
            sb.AppendLine($"- {c.Card.Phy}: driver {c.Card.Module ?? c.Card.Driver ?? "?"}, {c.Card.Bus}{ids}, catálogo: {c.Status.ToString().ToLowerInvariant()}{(c.Card.IsInternetConnection ? ", é a internet do PC" : "")}");
        }
        sb.AppendLine();
        sb.AppendLine("### Portas seriais");
        if (s.SerialPorts.Count == 0)
            sb.AppendLine("- nenhuma");
        foreach (var p in s.SerialPorts)
            sb.AppendLine($"- {p.Path}: {p.VendorId ?? "?"}:{p.ProductId ?? "?"} {p.ProductName}{(p.DriverMissing ? " (sem driver)" : "")}");
        sb.AppendLine();
        sb.AppendLine("### Configuração");
        sb.AppendLine($"- Rádio escolhido: {status.State.Radio ?? "nenhum"}{(status.State.Phy is { } phy ? $" ({phy})" : "")}{(status.State.Esp32Board is { } b ? $" ({b})" : "")}");
        if (status.State.Esp32Probe is { } probe)
            sb.AppendLine($"- ESP32: firmware {probe.Code}{(probe.Target is { } target ? $" ({target})" : "")}{(probe.Simulated ? " (simulado)" : "")}");
        if (status.State.Esp32Chip is { } chip)
            sb.AppendLine($"- ESP32: chip {chip.Description}, flash {chip.FlashSize ?? "?"}");
        if (status.State.Esp32Firmware is { } fw)
            sb.AppendLine($"- ESP32: firmware {(fw.Matches ? "igual ao embutido" : "diferente do embutido")}{(fw.Written ? " (gravado pelo app)" : "")}");
        sb.AppendLine($"- prod.keys: {(status.Keys is null ? "não enviado" : status.Keys.Valid ? "formato válido" : "formato inválido")}");
        sb.AppendLine($"- Componente de rede: {(s.Worker is { } w ? $"instalado ({w.Flavor}{(w.Safe ? "" : ", inseguro")}{(w.PolicyInstalled ? "" : ", sem política polkit")}{(w.PkexecFound ? "" : ", sem pkexec")})" : "não instalado")}");
        if (s.MissingTools is { Count: > 0 } tools)
            sb.AppendLine($"- Ferramentas faltando: {string.Join(", ", tools)}");
        if (s.LdnNetwork is { } net)
            sb.AppendLine($"- Configuração de rede do LDN: NetworkManager {(net.NetworkManager ? "ok" : "não")}, udev {(net.UdevLink ? "ok" : "não")}");
        if (status.State.CardTest is { } test)
            sb.AppendLine($"- Teste da placa ({test.Phy}): {test.Code}{(test.Simulated ? " (simulado)" : "")} · " +
                string.Join(", ", test.Channels.Select(c => $"canal {c.Channel}: {(c.Set ? $"{c.Sent} enviados, {c.Received} recebidos" : "não sintonizou")}")));
        if (status.State.SwitchTest is { } sw)
        {
            var networks = sw.Networks.Count == 0 ? "nenhuma sessão decifrada"
                : string.Join(", ", sw.Networks.Select(n => $"{(n.Frlg ? "FRLG" : "outro jogo")} {n.CommId} no canal {n.Channel} (LDN {n.Protocol})"));
            var skipped = sw.Channels.Where(c => !c.Usable).Select(c => c.Channel).ToList();
            sb.AppendLine($"- Teste com o Switch ({sw.Phy}): {sw.Code}{(sw.Simulated ? " (simulado)" : "")} · {networks} · " +
                $"{sw.Undecryptable} anúncio(s) sem decifrar · {sw.Sweeps} volta(s) em {sw.Seconds:0.#} s" +
                (skipped.Count > 0 ? $" · canais indisponíveis: {string.Join(", ", skipped)}" : ""));
        }
        sb.AppendLine($"- Grupo da porta serial: {s.SerialGroup}{(s.User is { } u ? (u.Groups.Contains(s.SerialGroup) ? " (usuário no grupo)" : " (usuário fora do grupo)") : "")}");
        sb.AppendLine();
        sb.AppendLine("### Verificações");
        foreach (var c in status.Checks)
            sb.AppendLine($"- [{c.Status.ToString().ToLowerInvariant()}] etapa {c.Step} · {c.Id}: {c.Code}");
        return sb.ToString();
    }
}
