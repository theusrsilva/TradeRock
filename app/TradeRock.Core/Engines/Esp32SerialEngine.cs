using System.Text.Json;
using Frlg.Trade.Core;
using TradeRock.Core.Esp32;
using TradeRock.Core.Setup;
using TradeRock.Core.Trading;
using CoreTrainer = Frlg.Trade.Core.TrainerProfile;

namespace TradeRock.Core.Engines;

/// <summary>Uma sessão do núcleo do easyworld. O padrão é o TradeSession (com os patches); os testes usam um falso.</summary>
public interface IEsp32Session
{
    /// <summary>Bloqueia até a sessão acabar. Os eventos do núcleo chegam em <paramref name="emit"/>.</summary>
    void Run(Esp32SessionRequest request, Action<object> emit, CancellationToken ct);
}

public sealed record Esp32SessionRequest(
    string Port,
    byte[]?[] Party,
    int Selected,
    Func<int> CurrentSlot,
    string LogDir,
    string KeysPath,
    int TradeLimit,
    CoreTrainer Profile);

public sealed class UpstreamEsp32Session : IEsp32Session
{
    public void Run(Esp32SessionRequest r, Action<object> emit, CancellationToken ct) =>
        new TradeSession(emit).Run(r.Port, r.Party, r.Selected, r.LogDir, ct, r.CurrentSlot, null,
            new KeyFile(r.KeysPath), r.TradeLimit, r.Profile);
}

public sealed class Esp32EngineOptions
{
    /// <summary>Pasta dos logs do núcleo (native.log e a captura pia.jsonl de cada sessão).</summary>
    public required string LogsDir { get; init; }

    /// <summary>Quantas sessões guardar.</summary>
    public int KeepSessions { get; init; } = 10;
}

/// <summary>
/// Motor com o ESP32 no USB (Linux e Windows, sem root): o núcleo C# do easyworld conversa com o
/// firmware pela serial e roda o protocolo da troca. Uma chamada = uma tentativa (o retry é do
/// coordenador). Os marcos do núcleo viram os eventos do TradeRock.
/// </summary>
public sealed class Esp32SerialEngine(SetupService setup, ProdKeys keys, IEsp32Session session, Esp32EngineOptions options) : ITradeEngine
{
    public string Id => "esp32";

    public bool IsSimulation => false;

    public async Task<EngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var state = (await setup.StatusAsync(cancellationToken)).State;
        if (state.Radio != RadioAdvisor.Esp32 || state.SerialPort is null)
            return new EngineAvailability(false, "esp32_not_configured");
        return new EngineAvailability(true);
    }

    public async Task<TradeAttemptResult> RunAttemptAsync(TradeRequest request, Func<TradeEvent, ValueTask> emit, CancellationToken cancellationToken)
    {
        var state = (await setup.StatusAsync(cancellationToken)).State;
        if (state.SerialPort is not { } port)
            return TradeAttemptResult.Fail("esp32_not_configured", "Escolha a porta do ESP32 no assistente.", retryable: false);
        if (!File.Exists(keys.FilePath))
            return TradeAttemptResult.Fail("keys_missing", "O prod.keys não foi enviado.", retryable: false);

        var party = new byte[]?[6];
        for (var i = 0; i < request.Party.Count; i++)
            party[i] = request.Party[i].Data;
        var mapper = new Esp32EventMapper(request.OfferedSlots);
        var logDir = NewLogDir();

        // Os eventos saem da thread do núcleo; a fila os entrega em ordem para o coordenador.
        var events = System.Threading.Channels.Channel.CreateUnbounded<TradeEvent>();
        var sessionRequest = new Esp32SessionRequest(port, party, request.OfferedSlots[0], mapper.CurrentSlot, logDir,
            keys.FilePath, request.Trades,
            // TrainerId de 32 bits = TID | SID << 16; sem SID, o mesmo do padrão (0x47ED).
            new CoreTrainer(request.Trainer.Name,
                request.Trainer.Tid | ((uint)(request.Trainer.Sid ?? TradeRock.Core.Trading.TrainerProfile.Default.Sid ?? 0) << 16),
                request.Game == TradeGame.FireRed ? 4 : 5));

        var run = Task.Factory.StartNew(() =>
        {
            try
            {
                session.Run(sessionRequest, e => { if (mapper.Map(e) is { } ev) events.Writer.TryWrite(ev); }, cancellationToken);
            }
            finally
            {
                events.Writer.TryComplete();
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        await foreach (var e in events.Reader.ReadAllAsync(CancellationToken.None))
            await emit(e);

        try
        {
            await run;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TradeAttemptResult.Cancelled;
        }
        catch (Exception e)
        {
            return Failure(e, mapper.Joined);
        }
        return mapper.Received >= request.Trades
            ? TradeAttemptResult.Completed
            : mapper.Received > 0
                ? TradeAttemptResult.Fail("trade_incomplete", $"{mapper.Received}/{request.Trades}", retryable: false)
                : TradeAttemptResult.Fail(mapper.Joined ? "connection_lost" : "join_failed", "", retryable: !mapper.Joined);
    }

    private string NewLogDir()
    {
        Directory.CreateDirectory(options.LogsDir);
        foreach (var old in Directory.GetDirectories(options.LogsDir).OrderByDescending(d => d).Skip(options.KeepSessions - 1))
        {
            try { Directory.Delete(old, true); } catch (IOException) { }
        }
        return Path.Combine(options.LogsDir, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
    }

    /// <summary>Exceção do núcleo (mensagens em chinês no upstream) → código estável.</summary>
    public static TradeAttemptResult Failure(Exception e, bool joined)
    {
        var (code, retryable) = e switch
        {
            ConnectionException c when c.Message.Contains(Esp32Messages.NoRoom) => ("no_network_found", true),
            ConnectionException c when c.Message.Contains(Esp32Messages.AuthTimeout) => ("join_not_approved", true),
            ConnectionException c when c.Message.Contains(Esp32Messages.AssociationRetries) => ("join_failed", true),
            ConnectionException c when c.Message.Contains(Esp32Messages.NoProtocol) => ("esp32_firmware_missing", false),
            ConnectionException c when c.Message.Contains(Esp32Messages.Incapable) => ("esp32_firmware_incompatible", false),
            ConnectionException c when c.Message.Contains(Esp32Messages.WirelessLost) || c.Message.Contains(Esp32Messages.HostTimeout) => ("connection_lost", !joined),
            ConnectionException c when c.Message.Contains(Esp32Messages.RoomChanged) || c.Message.Contains(Esp32Messages.MembersChanged) => ("room_changed", !joined),
            TimeoutException => ("esp32_no_response", !joined),
            UnauthorizedAccessException => ("serial_permission", false),
            IOException io when io.Message.Contains("busy", StringComparison.OrdinalIgnoreCase) => ("serial_busy", false),
            FileNotFoundException => ("keys_missing", false),
            InvalidDataException => ("engine_error", false),
            IOException => ("serial_port_missing", false),
            _ => ("engine_error", false),
        };
        return TradeAttemptResult.Fail(code, e.Message, retryable && !joined);
    }
}

/// <summary>
/// Eventos do TradeSession do easyworld ({ event = "phase" | "log" | "connected" | "opponent_party" |
/// "offer_locked" | "received" | ... }) → eventos do TradeRock. Fases e logs são reconhecidos pelos
/// textos de <see cref="Esp32Messages"/>; o resto do log detalhado fica no native.log da sessão.
/// </summary>
public sealed class Esp32EventMapper(IReadOnlyList<int> offeredSlots)
{
    private int _received;
    private bool _menuAnnounced;

    public int Received => _received;
    public bool Joined { get; private set; }

    /// <summary>Posição oferecida na próxima troca (a do número de trocas já feitas).</summary>
    public int CurrentSlot() => offeredSlots[Math.Min(Volatile.Read(ref _received), offeredSlots.Count - 1)];

    public TradeEvent? Map(object raw)
    {
        var e = JsonSerializer.SerializeToElement(raw);
        var name = e.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        var message = e.TryGetProperty("message", out var m) ? m.GetString() : null;
        TradeEvent Make(TradeEventKind kind, string? text = null, int? index = null, byte[]? data = null) =>
            new(kind, DateTimeOffset.UtcNow, text ?? message) { TradeIndex = index, ReceivedData = data };

        switch (name)
        {
            case "phase" when message == Esp32Messages.PhaseScanning:
                return Make(TradeEventKind.Scanning, "Procurando a sala do Switch (canais de 2,4 GHz)...");
            case "phase" when message == Esp32Messages.PhaseJoining:
                return Make(TradeEventKind.JoinRequested, "Entrando na sala: aprove o pedido no Switch.");
            case "phase" when message == Esp32Messages.PhaseLeaving:
                return Make(TradeEventKind.Leaving, "Trocas concluídas; aguardando o Switch sair da sala.");
            case "log" when message?.StartsWith(Esp32Messages.RoomVerified, StringComparison.Ordinal) == true:
                return Make(TradeEventKind.BeaconDecrypted);
            case "log" when message == Esp32Messages.HostEntered:
                return Make(TradeEventKind.InRoom, "O jogador entrou na sala de troca.");
            case "log" when message?.StartsWith(Esp32Messages.LinkClosed, StringComparison.Ordinal) == true:
                return Make(TradeEventKind.LinkClosed);
            case "device":
                return Make(TradeEventKind.Log, $"ESP32: {(e.TryGetProperty("model", out var model) ? model.GetString() : "?")}");
            case "connected":
                Joined = true;
                return Make(TradeEventKind.Joined, "Entrou na sala.");
            case "opponent_party" when !_menuAnnounced:
                _menuAnnounced = true;
                return Make(TradeEventKind.TradeMenuOpen, "Menu de troca aberto; time do Switch recebido.");
            case "offer_locked":
                return Make(TradeEventKind.TradeOffered, "Pokémon oferecido; aguardando o Switch.", Volatile.Read(ref _received));
            case "received" when e.TryGetProperty("pk3", out var pk3) && pk3.GetString() is { } hex:
                var index = Interlocked.Increment(ref _received) - 1;
                return Make(TradeEventKind.TradeDone, "Troca confirmada.", index, Convert.FromHexString(hex));
            default:
                return null;
        }
    }
}
