using System.Text;

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

    /// <summary>ESP32 clássico: a pessoa conferiu "ESP32-WROOM-32E" na blindagem do módulo.</summary>
    public bool Esp32ModuleConfirmed { get; init; }

    public bool KeysSkipped { get; init; }
    public bool SwitchTestPassed { get; init; }
    public DateTimeOffset? SwitchTestAt { get; init; }
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

        // Etapa 6: teste com o Switch (beacon encontrado e decifrado)
        checks.Add(state.SwitchTestPassed
            ? new SetupCheck("switch_test", 6, CheckStatus.Ok, true, "switch_test_ok")
            : new SetupCheck("switch_test", 6, CheckStatus.Pending, true, "switch_test_pending",
                AvailableIn: state.Radio == RadioAdvisor.Esp32 ? "entrega-9" : "entrega-7"));

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

        yield return new SetupCheck("worker", 4, CheckStatus.Pending, true, "worker_pending", AvailableIn: "entrega-6");

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

        // O teste real (modo monitor + action frames) é o que diz se a placa serve.
        yield return new SetupCheck("card_test", 4, CheckStatus.Pending, true, "card_test_pending", cardArgs, AvailableIn: "entrega-6");

        if (system.NetworkManagerRunning)
            yield return new SetupCheck("network_manager", 4, CheckStatus.Pending, false, "network_manager_pending", AvailableIn: "entrega-6");
    }

    private static IEnumerable<SetupCheck> Esp32Checks(SystemSnapshot system, SetupState state, HardwareCatalog catalog)
    {
        // Placa informada pela pessoa (a leitura do chip via esptool chega na Entrega 9).
        var board = catalog.Esp32.FirstOrDefault(b => b.Id == state.Esp32Board);
        if (board is null)
            yield return new SetupCheck("esp32_board", 4, CheckStatus.Error, true, "esp32_board_not_chosen");
        else if (board.Status == CompatStatus.Unsupported)
            yield return new SetupCheck("esp32_board", 4, CheckStatus.Error, true, "esp32_board_unsupported", Args(("model", board.Model)));
        else if (board.Module == "ESP32-WROOM-32E" && !state.Esp32ModuleConfirmed)
            yield return new SetupCheck("esp32_board", 4, CheckStatus.Error, true, "esp32_confirm_module");
        else
            yield return new SetupCheck("esp32_board", 4, board.Status == CompatStatus.Untested ? CheckStatus.Warning : CheckStatus.Ok,
                board.Status != CompatStatus.Untested, $"esp32_board_{Name(board.Status)}", Args(("model", board.Model)));

        var port = system.SerialPorts.FirstOrDefault(p => p.Path == state.SerialPort);
        if (system.SerialPorts.Count == 0)
            yield return new SetupCheck("serial_port", 4, CheckStatus.Error, true, "serial_none");
        else if (port is null)
            yield return new SetupCheck("serial_port", 4, CheckStatus.Error, true, "serial_not_chosen", Args(("count", system.SerialPorts.Count.ToString())));
        else
        {
            var chip = catalog.MatchUsbSerial(port.VendorId, port.ProductId);
            yield return new SetupCheck("serial_port", 4, CheckStatus.Ok, true, "serial_ok",
                Args(("port", port.Path), ("chip", chip?.Name ?? port.ProductName ?? "?")));

            if (port.DriverMissing)
                yield return new SetupCheck("usb_driver", 4, CheckStatus.Error, true, "usb_driver_missing",
                    Args(("chip", chip?.Name ?? "?"), ("url", chip?.DriverUrl ?? "")));

            // Conferência da porta com o tipo de placa: C3 usa USB nativa; S3/C6/clássico, o conversor.
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

        yield return new SetupCheck("esp32_firmware", 4, CheckStatus.Pending, true, "esp32_firmware_pending", AvailableIn: "entrega-9");
        yield return new SetupCheck("esp32_serial_test", 4, CheckStatus.Pending, true, "esp32_serial_test_pending", AvailableIn: "entrega-9");
    }

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
        sb.AppendLine($"- prod.keys: {(status.Keys is null ? "não enviado" : status.Keys.Valid ? "formato válido" : "formato inválido")}");
        sb.AppendLine($"- Grupo da porta serial: {s.SerialGroup}{(s.User is { } u ? (u.Groups.Contains(s.SerialGroup) ? " (usuário no grupo)" : " (usuário fora do grupo)") : "")}");
        sb.AppendLine();
        sb.AppendLine("### Verificações");
        foreach (var c in status.Checks)
            sb.AppendLine($"- [{c.Status.ToString().ToLowerInvariant()}] etapa {c.Step} · {c.Id}: {c.Code}");
        return sb.ToString();
    }
}
