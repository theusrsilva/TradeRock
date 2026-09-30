using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using QRCoder;
using TradeRock.Core.Setup;
using TradeRock.Core.Storage;

namespace TradeRock.App.Server;

public sealed class ServerModeException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Escolhas guardadas (a interface e a porta). O modo servidor em si começa desligado a cada abertura do app.</summary>
public sealed record ServerModeSettings(string? Interface = null, int Port = ServerModeSettings.DefaultPort)
{
    public const int DefaultPort = 8337;
}

/// <summary>Uma interface de rede com IPv4. <see cref="Blocked"/> = é a placa Wi-Fi que faz as trocas.</summary>
public sealed record LanInterface(string Name, string Address, bool Wireless, bool Blocked);

public sealed record ServerModeStatus(
    bool Enabled,
    string? Interface,
    int Port,
    string? Url,
    string Radio, // wifi | esp32 | none
    IReadOnlyList<LanInterface> Interfaces,
    int Sessions);

/// <summary>Interfaces de rede do PC (trocável nos testes).</summary>
public interface INetworkInterfaces
{
    IReadOnlyList<(string Name, IPAddress Address, bool Wireless)> List();
}

public sealed class SystemNetworkInterfaces : INetworkInterfaces
{
    public IReadOnlyList<(string Name, IPAddress Address, bool Wireless)> List() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => (n.Name, a.Address, n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)))
            .OrderBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
}

/// <summary>Liga e desliga o endereço da rede no servidor web (trocável nos testes).</summary>
public interface ILanListener
{
    /// <summary>False = não conseguiu escutar (porta ocupada, endereço sumiu).</summary>
    Task<bool> StartAsync(IPAddress address, int port, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

/// <summary>
/// Kestrel: o endereço da rede entra como um endpoint da seção "Kestrel" da configuração, que o Kestrel
/// recarrega sozinho (reloadOnChange). O endereço local (127.0.0.1) continua como estava.
/// </summary>
public sealed class KestrelLanListener(LanConfigSource config, IServiceProvider services, ServerOptions options) : ILanListener
{
    public async Task<bool> StartAsync(IPAddress address, int port, CancellationToken ct)
    {
        var url = $"http://{address}:{port}";
        config.Apply(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Lan:Url"] = url,
            ["AllowedHosts"] = $"{options.AllowedHosts};{address}",
        });
        for (var i = 0; i < 50; i++)
        {
            if (Addresses().Any(a => a.StartsWith(url, StringComparison.OrdinalIgnoreCase)))
                return true;
            await Task.Delay(100, ct);
        }
        config.Apply(new Dictionary<string, string?>());
        return false;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        config.Apply(new Dictionary<string, string?>());
        for (var i = 0; i < 50 && Addresses().Count > 1; i++)
            await Task.Delay(100, ct);
    }

    private ICollection<string> Addresses() =>
        services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
}

/// <summary>Configuração que muda com o app aberto (endpoint da rede e hosts aceitos).</summary>
public sealed class LanConfigSource : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public void Apply(IDictionary<string, string?> values)
    {
        Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}

/// <summary>
/// Modo servidor: a mesma interface servida para a rede de casa, com contas. Só escuta no IPv4 da interface
/// escolhida (nunca em todas) e nunca na placa Wi-Fi que faz as trocas: ela sai da rede durante a troca.
/// </summary>
public sealed class ServerMode(ILanListener listener, INetworkInterfaces network, SettingsStore settings, SetupService setup,
    LanSessions sessions, ILogger<ServerMode> logger)
{
    public const string SettingsKey = "server.mode";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Active? _active;

    private sealed record Active(string Interface, IPAddress Address, int Port);

    public bool IsEnabled => _active is not null;

    /// <summary>A requisição chegou pelo endereço da rede?</summary>
    public bool IsLanRequest(HttpContext context)
    {
        var active = _active;
        if (active is null)
            return false;
        var local = context.Connection.LocalIpAddress;
        if (local is { IsIPv4MappedToIPv6: true })
            local = local.MapToIPv4();
        return context.Connection.LocalPort == active.Port && active.Address.Equals(local);
    }

    public string? Url => _active is { } a ? $"http://{a.Address}:{a.Port}/" : null;

    public async Task<ServerModeStatus> StatusAsync(CancellationToken ct)
    {
        var saved = await settings.GetAsync(SettingsKey, new ServerModeSettings(), ct);
        var (radio, interfaces) = await InterfacesAsync(ct);
        var active = _active;
        return new ServerModeStatus(active is not null, active?.Interface ?? saved.Interface, active?.Port ?? saved.Port, Url,
            radio, interfaces, sessions.Count);
    }

    public async Task<ServerModeStatus> EnableAsync(string interfaceName, int port, int localPort, CancellationToken ct)
    {
        if (port is < 1024 or > 65535 || port == localPort)
            throw new ServerModeException("port_invalid");

        await _gate.WaitAsync(ct);
        try
        {
            var (_, interfaces) = await InterfacesAsync(ct);
            var chosen = interfaces.FirstOrDefault(i => i.Name == interfaceName) ?? throw new ServerModeException("interface_not_found");
            if (chosen.Blocked)
                throw new ServerModeException("interface_is_radio");

            if (_active is not null)
                await StopAsync(ct);

            var address = IPAddress.Parse(chosen.Address);
            if (!await listener.StartAsync(address, port, ct))
                throw new ServerModeException("bind_failed");
            _active = new Active(chosen.Name, address, port);
            await settings.SetAsync(SettingsKey, new ServerModeSettings(chosen.Name, port), ct);
            logger.LogInformation("Modo servidor ligado em {Address}:{Port}.", address, port);
        }
        finally
        {
            _gate.Release();
        }
        return await StatusAsync(ct);
    }

    public async Task<ServerModeStatus> DisableAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is not null)
            {
                await StopAsync(ct);
                logger.LogInformation("Modo servidor desligado.");
            }
        }
        finally
        {
            _gate.Release();
        }
        return await StatusAsync(ct);
    }

    /// <summary>A interface em uso seria bloqueada se a placa Wi-Fi do assistente virasse o rádio das trocas?</summary>
    public async Task<bool> ConflictsWithWifiAsync(CancellationToken ct)
    {
        var active = _active;
        if (active is null)
            return false;
        var blocked = await RadioInterfacesAsync(forceWifi: true, ct);
        return blocked.Contains(active.Interface);
    }

    private async Task StopAsync(CancellationToken ct)
    {
        _active = null;
        sessions.Clear();
        await listener.StopAsync(ct);
    }

    private async Task<(string Radio, IReadOnlyList<LanInterface> Interfaces)> InterfacesAsync(CancellationToken ct)
    {
        var status = await setup.StatusAsync(ct);
        var radio = status.State.Radio ?? "none";
        var blocked = await RadioInterfacesAsync(forceWifi: false, ct);
        var list = network.List()
            .Select(n => new LanInterface(n.Name, n.Address.ToString(), n.Wireless, blocked.Contains(n.Name)
                || (radio == "wifi" && status.State.Phy is null && n.Wireless)))
            .ToList();
        return (radio, list);
    }

    /// <summary>Interfaces da placa Wi-Fi escolhida no assistente, se ela for o rádio das trocas.</summary>
    private async Task<IReadOnlySet<string>> RadioInterfacesAsync(bool forceWifi, CancellationToken ct)
    {
        var status = await setup.StatusAsync(ct);
        if (!forceWifi && status.State.Radio != "wifi")
            return new HashSet<string>();
        var card = status.System.WifiCards.FirstOrDefault(c => c.Phy == status.State.Phy);
        return card?.Interfaces.ToHashSet() ?? [];
    }

    /// <summary>QR code (SVG) do endereço, para abrir no celular.</summary>
    public static string QrSvg(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(8, "#000000", "#ffffff", true, SvgQRCode.SizingMode.ViewBoxAttribute, null);
    }
}
