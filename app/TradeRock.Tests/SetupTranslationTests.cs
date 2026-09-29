using System.Text.Json;
using TradeRock.Core.Setup;

namespace TradeRock.Tests;

/// <summary>Todo código de verificação e situação que o backend gera tem texto nos dois idiomas.</summary>
public sealed class SetupTranslationTests
{
    private static JsonElement Locale(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "TradeRock.slnx")))
            dir = Path.GetDirectoryName(dir)!;
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "frontend", "src", "locales", $"{lang}.json"))).RootElement.GetProperty("wizard");
    }

    private static IEnumerable<SetupCheck> AllChecks()
    {
        var catalog = HardwareCatalog.Embedded;
        var keysOptions = new KeysValidation?[] { null, new(true, [], [], [], [1], 3), new(false, [2], [], ["master_key_00"], [], 1) };
        foreach (var (_, system) in SetupScenarios.All)
        {
            var advice = RadioAdvisor.Advise(system, catalog);
            var phys = system.WifiCards.Select(c => (string?)c.Phy).Append(null);
            var ports = system.SerialPorts.Select(p => (string?)p.Path).Append(null);
            var boards = catalog.Esp32.Select(b => (string?)b.Id).Append(null);
            foreach (var keys in keysOptions)
            foreach (var radio in new[] { null, "wifi", "esp32" })
            foreach (var phy in phys)
            foreach (var port in ports)
            foreach (var board in boards)
            foreach (var confirmed in new[] { false, true })
            {
                var state = new SetupState { Radio = radio, Phy = phy, SerialPort = port, Esp32Board = board, Esp32ModuleConfirmed = confirmed, KeysSkipped = keys is null && confirmed };
                foreach (var check in SetupChecks.Evaluate(system, advice, state, keys, catalog))
                    yield return check;
            }
        }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en")]
    public void Codigos_de_verificacao_tem_texto(string lang)
    {
        var checks = Locale(lang).GetProperty("checks");
        var missing = AllChecks().Select(c => c.Code).Distinct().Where(code => !checks.TryGetProperty(code, out _)).ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en")]
    public void Situacoes_motivos_e_entregas_tem_texto(string lang)
    {
        var w = Locale(lang);
        var catalog = HardwareCatalog.Embedded;
        foreach (var system in SetupScenarios.All.Values)
        {
            var advice = RadioAdvisor.Advise(system, catalog);
            Assert.True(w.GetProperty("preview").TryGetProperty(advice.Situation, out _), advice.Situation);
            foreach (var reason in advice.Options.Select(o => o.ReasonCode).OfType<string>())
                Assert.True(w.GetProperty("reasons").TryGetProperty(reason, out _), reason);
        }
        foreach (var delivery in AllChecks().Select(c => c.AvailableIn).OfType<string>().Distinct())
            Assert.True(w.GetProperty("deliveries").TryGetProperty(delivery, out _), delivery);
    }
}
