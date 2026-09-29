namespace TradeRock.Core.Setup;

/// <summary>Uma placa Wi-Fi detectada, com o que o catálogo diz dela.</summary>
public sealed record CardAssessment(WifiCard Card, CompatStatus Status, IReadOnlyList<WifiEntry> Matches)
{
    public bool InCatalog => Matches.Count > 0;
}

public sealed record RadioOption(string Engine, bool Available, bool Recommended, string? ReasonCode);

/// <summary>
/// Recomendação de "rádio" (placa Wi-Fi do PC ou ESP32). Situation:
/// windows | wsl | vm | no_wifi | wifi_compatible | wifi_test_first | wifi_problematic.
/// </summary>
public sealed record RadioAdvice(
    string Situation,
    IReadOnlyList<RadioOption> Options,
    IReadOnlyList<CardAssessment> Cards,
    CardAssessment? BestCard,
    IReadOnlyList<WifiEntry> SuggestedUsbAdapters);

public static class RadioAdvisor
{
    public const string Wifi = "wifi";
    public const string Esp32 = "esp32";

    public static CardAssessment Assess(WifiCard card, HardwareCatalog catalog)
    {
        var matches = catalog.MatchWifi(card.Module, card.Driver);
        // Mesmo driver em vários modelos (ex.: iwlwifi): vale o pior status, para não prometer demais.
        var status = matches.Count == 0 ? CompatStatus.Untested : matches.Max(m => m.Status);
        return new CardAssessment(card, status, matches);
    }

    public static RadioAdvice Advise(SystemSnapshot system, HardwareCatalog catalog)
    {
        var cards = system.WifiCards.Select(c => Assess(c, catalog)).ToList();
        // Melhor placa: melhor status; empate → a que não é a internet do PC, depois USB.
        var best = cards
            .OrderBy(c => c.Status)
            .ThenBy(c => c.Card.IsInternetConnection)
            .ThenBy(c => c.Card.Bus == "usb" ? 0 : 1)
            .FirstOrDefault();

        var usbAdapters = catalog.Wifi
            .Where(w => w.Type == "usb" && w.Status is CompatStatus.Recommended or CompatStatus.Works)
            .OrderBy(w => w.Status)
            .ToList();

        RadioAdvice Build(string situation, bool wifiAvailable, bool recommendWifi, string? wifiReason, bool suggestAdapters) => new(
            situation,
            [
                new RadioOption(Wifi, wifiAvailable, recommendWifi, wifiReason),
                new RadioOption(Esp32, true, !recommendWifi, null),
            ],
            cards,
            best,
            suggestAdapters ? usbAdapters : []);

        if (system.Os == "windows")
            return new RadioAdvice("windows", [new RadioOption(Wifi, false, false, "windows"), new RadioOption(Esp32, true, true, null)], cards, null, []);
        if (system.IsWsl)
            return Build("wsl", false, false, "wsl", false);
        if (system.VirtualMachine is not null)
            return Build("vm", false, false, "vm", false);
        if (best is null)
            return Build("no_wifi", false, false, "no_wifi", true);

        return best.Status switch
        {
            CompatStatus.Recommended or CompatStatus.Works => Build("wifi_compatible", true, true, null, false),
            CompatStatus.Untested or CompatStatus.Unstable => Build("wifi_test_first", true, true, best.Status == CompatStatus.Unstable ? "unstable" : "untested", true),
            _ => Build("wifi_problematic", false, false, "problematic", true),
        };
    }
}
