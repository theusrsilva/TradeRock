using System.Diagnostics;
using System.Text;
using Frlg.Trade.Core;

namespace TradeRock.Core.Esp32;

/// <summary>Erro do ESP32. <see cref="Code"/> é estável (traduzido no frontend).</summary>
public sealed class Esp32Exception(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Resposta do firmware ao LDN_HELLO (docs/SERIAL_PROTOCOL.md do easyworld).</summary>
public sealed record Esp32Hello(string Target);

/// <summary>Uma sala vista pelo ESP32 no teste com o Switch.</summary>
public sealed record Esp32Room(ulong CommunicationId, int Channel, int Protocol, int Members, int Maximum, int Policy);

public sealed record Esp32ScanResult(IReadOnlyList<Esp32Room> Rooms, int Undecryptable, IReadOnlyList<int> UndecryptableChannels,
    int Sweeps, double Seconds, bool FoundFrlg);

/// <summary>Conversa com o firmware pela porta serial, sem entrar em sala nenhuma.</summary>
public interface IEsp32Link
{
    /// <summary>LDN_HELLO + LDN_BEGIN + LDN_STOP: há firmware compatível e a comunicação funciona?</summary>
    Esp32Hello Probe(string port, CancellationToken ct);

    /// <summary>Escuta a sala do FRLG nos canais de 2,4 GHz e tenta decifrar o anúncio com o prod.keys.</summary>
    Esp32ScanResult Scan(string port, string keysPath, TimeSpan limit, CancellationToken ct);
}

public sealed class SerialEsp32Link : IEsp32Link
{
    /// <summary>Salas do FRLG que o motor aceita (TradeSession.KnownIds, do patch do TradeRock).</summary>
    public static bool IsFrlg(ulong communicationId) => TradeSession.KnownIds.Contains(communicationId);

    /// <summary>Mesma ordem e mesmo tempo por canal da troca (TradeSession.Run): 500 ms em cada.</summary>
    public static readonly int[] Channels = [1, 6, 11, 2, 3, 4, 5, 7, 8, 9, 10];
    public const int DwellMs = 500;

    public Esp32Hello Probe(string port, CancellationToken ct) => WithDevice(port, ct, device => new Esp32Hello(device.Model));

    public Esp32ScanResult Scan(string port, string keysPath, TimeSpan limit, CancellationToken ct)
    {
        KeyFile keys;
        try
        {
            keys = new KeyFile(keysPath);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            throw new Esp32Exception("keys_invalid", "prod.keys inválido.");
        }

        return WithDevice(port, ct, device =>
        {
            var rooms = new List<Esp32Room>();
            var undecryptableChannels = new SortedSet<int>();
            int undecryptable = 0, sweeps = 0;
            int? stopAfterSweep = null; // com chaves erradas, esperar não muda nada: mais uma volta e para
            var watch = Stopwatch.StartNew();
            bool found = false;
            while (!found && watch.Elapsed < limit && (stopAfterSweep is null || sweeps < stopAfterSweep))
            {
                foreach (var channel in Channels)
                {
                    ct.ThrowIfCancellationRequested();
                    device.Command($"LDN_SCAN {channel}");
                    var dwell = Stopwatch.StartNew();
                    while (dwell.ElapsedMilliseconds < DwellMs && !found)
                    {
                        foreach (var frame in device.Drain())
                        {
                            if (Advertisement(frame, keys, out var network) is not { } decoded)
                                continue;
                            if (!decoded)
                            {
                                undecryptable++;
                                undecryptableChannels.Add(channel);
                                stopAfterSweep ??= sweeps + 2;
                                continue;
                            }
                            if (rooms.Any(r => r.CommunicationId == network!.CommunicationId && r.Channel == network.Channel))
                                continue;
                            rooms.Add(new Esp32Room(network!.CommunicationId, network.Channel, network.Protocol,
                                network.Members.Count, network.Maximum, network.Policy));
                            if (IsFrlg(network.CommunicationId))
                                found = true;
                        }
                        Thread.Sleep(2);
                    }
                    if (found || watch.Elapsed >= limit)
                        break;
                }
                sweeps++;
            }
            return new Esp32ScanResult(rooms, undecryptable, [.. undecryptableChannels], sweeps, watch.Elapsed.TotalSeconds, found);
        });
    }

    /// <summary>
    /// "LDN_ADV &lt;mac&gt; &lt;canal&gt; &lt;hex&gt;" (TradeSession.Advertisement): true = decifrou, false = é um
    /// anúncio LDN que não decifra com estas chaves, null = outro quadro.
    /// </summary>
    private static bool? Advertisement(SerialFrame frame, KeyFile keys, out LdnNetwork? network)
    {
        network = null;
        if (frame.Kind != 3)
            return null;
        var fields = Encoding.UTF8.GetString(frame.Payload).Split(' ');
        if (fields.Length != 4 || fields[0] != "LDN_ADV")
            return null;
        try
        {
            network = LdnKeys.Decode(keys, Bin.Hex(fields[3]), Bin.Hex(fields[1].Replace(":", "")), int.Parse(fields[2]));
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or ArgumentException
                                      or Org.BouncyCastle.Crypto.InvalidCipherTextException
                                      or System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Abre a porta, faz o handshake e sempre encerra a sessão do firmware no fim.</summary>
    private static T WithDevice<T>(string port, CancellationToken ct, Func<SerialDevice, T> body)
    {
        // No Linux o SerialPort acusa "sem permissão" também para uma porta que não existe.
        if (!OperatingSystem.IsWindows() && !File.Exists(port))
            throw new Esp32Exception("serial_port_missing", "A porta serial não existe mais. O ESP32 foi desconectado?");
        SerialDevice device;
        try
        {
            device = new SerialDevice(port, ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw new Esp32Exception("serial_permission", "Sem permissão para abrir a porta serial.");
        }
        catch (IOException e) when (e.Message.Contains("busy", StringComparison.OrdinalIgnoreCase))
        {
            throw new Esp32Exception("serial_busy", "A porta serial está em uso por outro programa.");
        }
        catch (IOException)
        {
            throw new Esp32Exception("serial_port_missing", "A porta serial não existe mais. O ESP32 foi desconectado?");
        }
        catch (ArgumentException)
        {
            throw new Esp32Exception("serial_port_missing", "Porta serial inválida.");
        }

        using (device)
        {
            var started = false;
            try
            {
                device.Handshake();
                started = true;
                return body(device);
            }
            catch (ConnectionException e)
            {
                throw Map(e);
            }
            catch (TimeoutException)
            {
                throw new Esp32Exception("esp32_no_response", "O ESP32 parou de responder.");
            }
            finally
            {
                if (started)
                {
                    try { device.Stop(); } catch (Exception e) when (e is TimeoutException or IOException or OperationCanceledException) { }
                }
            }
        }
    }

    /// <summary>Mensagens do SerialDevice.Handshake (em chinês no upstream) → código.</summary>
    public static Esp32Exception Map(ConnectionException e) => e.Message switch
    {
        var m when m.Contains(Esp32Messages.NoProtocol, StringComparison.Ordinal) =>
            new Esp32Exception("esp32_firmware_missing", "Nenhum firmware do TradeRock respondeu nesta porta."),
        var m when m.Contains(Esp32Messages.Incapable, StringComparison.Ordinal) =>
            new Esp32Exception("esp32_firmware_incompatible", "O firmware respondeu, mas não tem o que a troca precisa."),
        _ => new Esp32Exception("esp32_error", e.Message),
    };
}

/// <summary>
/// Trechos das mensagens do núcleo do easyworld (host/core) que o TradeRock reconhece. Um teste confere
/// que cada um ainda existe no código do submódulo.
/// </summary>
public static class Esp32Messages
{
    // SerialProtocol.cs (SerialDevice.Handshake)
    public const string NoProtocol = "设备未提供串口协议 v1";
    public const string Incapable = "设备能力不满足交易要求";
    public const string NoResponse = "设备未响应";

    // TradeSession.cs (fases e falhas da troca)
    public const string PhaseScanning = "正在搜索 Leader 房间";
    public const string PhaseJoining = "正在关联并认证房间";
    public const string PhaseLeaving = "交易已结束，等待 Leader 离房";
    public const string RoomVerified = "Room verified:";
    public const string LinkClosed = "Link closed:";
    public const string NoRoom = "没有找到可加入的火红 Leader 房间";
    public const string AuthTimeout = "房间认证超时";
    public const string AssociationRetries = "无线关联反复断开";
    public const string WirelessLost = "无线连接已断开";
    public const string HostTimeout = "主机通信超时";
    public const string RoomChanged = "Leader 房间已变化";
    public const string MembersChanged = "房间成员发生变化";

    // TradeEngine.cs
    public const string HostEntered = "Host entered the room";
}
