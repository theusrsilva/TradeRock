using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Frlg.Trade.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TradeRock.App;
using TradeRock.Core;
using TradeRock.Core.Engines;
using TradeRock.Core.Esp32;
using TradeRock.Core.Setup;
using TradeRock.Core.Trading;

namespace TradeRock.Tests;

internal static class Repo
{
    public static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "TradeRock.slnx")))
                dir = Path.GetDirectoryName(dir)!;
            return dir;
        }
    }

    public static string EspCore => Path.Combine(Root, "engines", "frlg-ldn-trade-esp32", "host", "core");
}

/// <summary>O patch do TradeRock no núcleo do easyworld e as mensagens que o app reconhece.</summary>
public class Esp32CorePatchTests
{
    [Fact]
    public void Treinador_padrao_reproduz_os_bytes_originais()
    {
        // Bytes das funções originais (Rfu.cs do submódulo, antes do patch): EMU / 0x47ED8822 / LeafGreen.
        var player = Rfu.PlayerBlock();
        Assert.Equal(0x4005, BitConverter.ToUInt16(player, 16));
        Assert.Equal(0x47ed8822u, BitConverter.ToUInt32(player, 20));
        Assert.Equal(Rfu.Name("EMU", 8), player[24..32]);
        var card = Rfu.TrainerCard();
        Assert.Equal(0x8822, BitConverter.ToUInt16(card, 14));
        Assert.Equal(5, card[56]);
        var ni = Rfu.GameData().ToArray();
        Assert.Equal(6, ni.Length);
    }

    [Fact]
    public void Treinador_do_tradeRock_vai_para_os_blocos()
    {
        var profile = new Frlg.Trade.Core.TrainerProfile("Ro-ch.", 12345u | (54321u << 16), 4); // FireRed
        var player = Rfu.PlayerBlock(profile);
        Assert.Equal(0x4004, BitConverter.ToUInt16(player, 16));
        Assert.Equal(12345u | (54321u << 16), BitConverter.ToUInt32(player, 20));
        // Pontuação da tabela Gen III (frlgsim/charmap.py): '-' 0xAE, '.' 0xAD.
        Assert.Equal(new byte[] { 0xcc, 0xe3, 0xae, 0xd7, 0xdc, 0xad, 0xff, 0x00 }, player[24..32]);
        var card = Rfu.TrainerCard(profile);
        Assert.Equal(12345, BitConverter.ToUInt16(card, 14));
        Assert.Equal(4, card[56]);
    }

    [Fact]
    public void Salas_do_frlg_conhecidas()
    {
        Assert.Contains(0x0100610011000000ul, TradeSession.KnownIds);
        Assert.Contains(TradeSession.FireRedId, TradeSession.KnownIds);
        Assert.True(SerialEsp32Link.IsFrlg(0x0100610011000000));
        Assert.False(SerialEsp32Link.IsFrlg(0x0100abcd00000000));
    }

    [Fact]
    public void Mensagens_reconhecidas_existem_no_nucleo()
    {
        var sources = string.Concat(Directory.GetFiles(Repo.EspCore, "*.cs").Select(File.ReadAllText));
        var missing = typeof(Esp32Messages).GetFields()
            .Select(f => (f.Name, Value: (string)f.GetValue(null)!))
            .Where(f => !sources.Contains(f.Value, StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();
        Assert.Empty(missing);
    }
}

public class Esp32FirmwareTests
{
    [Fact]
    public void Binarios_embutidos_conferem_e_batem_com_o_catalogo()
    {
        var hashes = Esp32Firmware.PublishedHashes();
        Assert.Equal(4, hashes.Count);
        foreach (var image in Esp32Firmware.Images)
        {
            var data = Esp32Firmware.Read(image);
            // Cabeçalho de imagem ESP do bootloader: em 0x1000 no ESP32 clássico, em 0x0 nos outros
            // (firmware/release/README.md, merge-bin); o binário completo é gravado em 0x0.
            Assert.Equal(0xe9, data[image.Target == "esp32" ? 0x1000 : 0]);
            Assert.Contains(image.File, hashes.Keys);
        }
        // Todo chip do catálogo que o firmware aceita tem um binário.
        foreach (var board in HardwareCatalog.Embedded.Esp32.Where(b => b.Status != CompatStatus.Unsupported))
            Assert.NotNull(Esp32Firmware.For(board.Chip));
    }

    [Fact]
    public void Extrair_grava_o_binario_conferido()
    {
        var dir = Directory.CreateTempSubdirectory("traderock-fw-").FullName;
        var image = Esp32Firmware.For("esp32s3")!;
        var path = Esp32Firmware.ExtractTo(image, dir);
        Assert.Equal(Esp32Firmware.Read(image), File.ReadAllBytes(path));
        Directory.Delete(dir, true);
    }
}

public class Esp32EventMapperTests
{
    [Fact]
    public void Eventos_do_nucleo_viram_eventos_do_tradeRock()
    {
        var mapper = new Esp32EventMapper([2, 0]);
        Assert.Equal(2, mapper.CurrentSlot());
        var pk3 = TradeRock.Core.Pokemon.DemoPokemon.All[0].Data;
        object[] raw =
        [
            new { @event = "run", path = "/x" },
            new { @event = "phase", message = Esp32Messages.PhaseScanning },
            new { @event = "log", message = Esp32Messages.RoomVerified + " protocol=1" },
            new { @event = "phase", message = Esp32Messages.PhaseJoining },
            new { @event = "connected" },
            new { @event = "log", message = Esp32Messages.HostEntered },
            new { @event = "opponent_party", name = "RED" },
            new { @event = "offer_locked", slot = 2 },
            new { @event = "received", slot = 2, pk3 = Convert.ToHexString(pk3) },
            new { @event = "opponent_party", name = "RED" },
            new { @event = "offer_locked", slot = 0 },
            new { @event = "received", slot = 0, pk3 = Convert.ToHexString(pk3) },
            new { @event = "log", message = "LINKCMD abcd cursor=0" },
            new { @event = "phase", message = Esp32Messages.PhaseLeaving },
            new { @event = "log", message = Esp32Messages.LinkClosed + " rx=1" },
        ];
        var events = raw.Select(mapper.Map).OfType<TradeEvent>().ToList();
        Assert.Equal(
            [TradeEventKind.Scanning, TradeEventKind.BeaconDecrypted, TradeEventKind.JoinRequested, TradeEventKind.Joined,
             TradeEventKind.InRoom, TradeEventKind.TradeMenuOpen, TradeEventKind.TradeOffered, TradeEventKind.TradeDone,
             TradeEventKind.TradeOffered, TradeEventKind.TradeDone, TradeEventKind.Leaving, TradeEventKind.LinkClosed],
            events.Select(e => e.Kind));
        var done = events.Where(e => e.Kind == TradeEventKind.TradeDone).ToList();
        Assert.Equal([0, 1], done.Select(e => e.TradeIndex!.Value));
        Assert.Equal(pk3, done[0].ReceivedData);
        Assert.True(mapper.Joined);
        Assert.Equal(0, mapper.CurrentSlot()); // depois da 1ª troca, a 2ª posição oferecida
    }

    [Theory]
    [InlineData("没有找到可加入的火红 Leader 房间，请确认主机仍在等待。", false, "no_network_found", true)]
    [InlineData("房间认证超时，请确认 Leader 房间并重试。", false, "join_not_approved", true)]
    [InlineData("设备未提供串口协议 v1，请先安装支持动态会话的新固件。", false, "esp32_firmware_missing", false)]
    [InlineData("无线连接已断开：LDN_LINK 0", true, "connection_lost", false)]
    [InlineData("无线连接已断开：LDN_LINK 0", false, "connection_lost", true)]
    public void Falhas_do_nucleo_viram_codigos(string message, bool joined, string code, bool retryable)
    {
        var result = Esp32SerialEngine.Failure(new ConnectionException(message), joined);
        Assert.Equal(code, result.Failure!.Code);
        Assert.Equal(retryable, result.Failure.Retryable);
    }
}

/// <summary>SerialDevice real (núcleo com patch) contra o firmware falso num pseudo-terminal.</summary>
public sealed class Esp32SerialLinkTests : IDisposable
{
    private readonly List<Process> _devices = [];
    private readonly string _dir = Directory.CreateTempSubdirectory("traderock-esp32-").FullName;

    public void Dispose()
    {
        foreach (var p in _devices)
        {
            try { p.Kill(); } catch (InvalidOperationException) { }
            p.Dispose();
        }
        Directory.Delete(_dir, true);
    }

    private string StartDevice(object config)
    {
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Repo.Root, "app", "TradeRock.Tests", "fake_esp32.py"));
        start.ArgumentList.Add(JsonSerializer.Serialize(config));
        var process = Process.Start(start)!;
        _devices.Add(process);
        return process.StandardOutput.ReadLine()!;
    }

    private static JsonElement Vectors =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.EspCore, "..", "tests", "fixtures", "vectors.json"))).RootElement;

    private string KeysFile()
    {
        var path = Path.Combine(_dir, "prod.keys");
        File.WriteAllLines(path, Vectors.GetProperty("keys").EnumerateObject().Select(p => $"{p.Name} = {p.Value.GetString()}"));
        return path;
    }

    [LinuxFact]
    public void Firmware_responde_ao_hello()
    {
        var port = StartDevice(new { mode = "ok", target = "esp32c6" });
        Assert.Equal("esp32c6", new SerialEsp32Link().Probe(port, CancellationToken.None).Target);
    }

    [LinuxTheory]
    [InlineData("silent", "esp32_firmware_missing")]
    [InlineData("incapable", "esp32_firmware_incompatible")]
    public void Sem_firmware_ou_incompativel(string mode, string code)
    {
        var port = StartDevice(new { mode });
        var e = Assert.Throws<Esp32Exception>(() => new SerialEsp32Link().Probe(port, CancellationToken.None));
        Assert.Equal(code, e.Code);
    }

    [LinuxFact]
    public void Porta_inexistente()
    {
        var e = Assert.Throws<Esp32Exception>(() => new SerialEsp32Link().Probe("/dev/ttyUSB-nao-existe", CancellationToken.None));
        Assert.Equal("serial_port_missing", e.Code);
    }

    [LinuxFact]
    public void Acha_e_decifra_a_sala()
    {
        var raw = Vectors.GetProperty("advertisements")[0].GetProperty("raw").GetString()!;
        var port = StartDevice(new { mode = "ok", adverts = new[] { new { channel = 6, mac = "02:00:00:00:00:01", hex = raw } } });
        var scan = new SerialEsp32Link().Scan(port, KeysFile(), TimeSpan.FromSeconds(20), CancellationToken.None);
        Assert.True(scan.FoundFrlg);
        var room = Assert.Single(scan.Rooms);
        Assert.Equal(6, room.Channel);
        Assert.Equal(0x0100610011000000ul, room.CommunicationId);
        var result = Esp32Setup.FromScan(port, scan, DateTimeOffset.UtcNow);
        Assert.Equal("found_decrypted", result.Code);
        Assert.True(result.Passed);
    }

    [LinuxFact]
    public void Anuncio_que_nao_decifra()
    {
        var raw = Vectors.GetProperty("advertisements")[0].GetProperty("raw").GetString()!;
        var tampered = raw[..^2] + (raw[^2..] == "00" ? "01" : "00");
        var port = StartDevice(new { mode = "ok", adverts = new[] { new { channel = 1, mac = "02:00:00:00:00:01", hex = tampered } } });
        var watch = Stopwatch.StartNew();
        var scan = new SerialEsp32Link().Scan(port, KeysFile(), TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.False(scan.FoundFrlg);
        Assert.True(scan.Undecryptable > 0);
        Assert.Contains(1, scan.UndecryptableChannels);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(25), "com chaves erradas, para depois de mais uma volta");
        Assert.Equal("found_not_decrypted", Esp32Setup.FromScan(port, scan, DateTimeOffset.UtcNow).Code);
    }
}

/// <summary>Sessão falsa do núcleo: emite eventos no formato do TradeSession.</summary>
internal sealed class FakeEsp32Session(string mode) : IEsp32Session
{
    public Esp32SessionRequest? Last { get; private set; }

    public void Run(Esp32SessionRequest r, Action<object> emit, CancellationToken ct)
    {
        Last = r;
        emit(new { @event = "phase", message = Esp32Messages.PhaseScanning });
        if (mode == "no_room")
            throw new ConnectionException(Esp32Messages.NoRoom + "，请确认主机仍在等待。");
        emit(new { @event = "log", message = Esp32Messages.RoomVerified });
        emit(new { @event = "phase", message = Esp32Messages.PhaseJoining });
        emit(new { @event = "connected" });
        if (mode == "hang")
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(10);
            }
        }
        emit(new { @event = "opponent_party", name = "RED" });
        for (var i = 0; i < r.TradeLimit; i++)
        {
            emit(new { @event = "offer_locked", slot = r.CurrentSlot() });
            emit(new { @event = "received", slot = r.CurrentSlot(), pk3 = Convert.ToHexString(TradeRock.Core.Pokemon.DemoPokemon.All[i % 2].Data) });
        }
        emit(new { @event = "phase", message = Esp32Messages.PhaseLeaving });
        emit(new { @event = "log", message = Esp32Messages.LinkClosed });
    }
}

file sealed class NoopLink : IEsp32Link
{
    public Esp32Hello Probe(string port, CancellationToken ct) => new("esp32s3");
    public Esp32ScanResult Scan(string port, string keysPath, TimeSpan limit, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>Do assistente (simulação linux-esp32) até a troca pela API, com a sessão do núcleo falsa.</summary>
public sealed class Esp32ApiTests : IAsyncLifetime
{
    private readonly SessionToken _token = SessionToken.Generate();
    private readonly string _dataDir = Directory.CreateTempSubdirectory("traderock-esp32api-").FullName;
    private readonly FakeEsp32Session _session = new("ok");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _app = TradeRockServer.Create(["--data-dir", _dataDir, "--environment", "Development"], _token, b =>
        {
            b.WebHost.UseTestServer();
            b.Services.AddSingleton<IEsp32Session>(_ => _session);
            b.Services.AddSingleton<IEsp32Link, NoopLink>();
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("http://127.0.0.1/");
        _client.DefaultRequestHeaders.Add(SessionAuthMiddleware.HeaderName, _token.Value);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        try { Directory.Delete(_dataDir, true); } catch (IOException) { }
    }

    private async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Check(JsonElement status, string id) =>
        status.GetProperty("checks").EnumerateArray().First(c => c.GetProperty("id").GetString() == id).GetProperty("code").GetString()!;

    private async Task<JsonElement> WaitFlashAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            var job = await Json(await _client.GetAsync("/api/setup/esp32/flash"));
            if (job.GetProperty("job").GetProperty("stage").GetString() is "done" or "error")
                return job;
            await Task.Delay(50);
        }
        throw new TimeoutException();
    }

    [Fact]
    public async Task Firmware_do_assistente_simulado()
    {
        await Json(await _client.PutAsJsonAsync("/api/setup/simulation", new { scenario = "linux-esp32" }));
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", esp32Board = "esp32-s3", serialPort = "/dev/ttyUSB0" }));

        var probe = await Json(await _client.PostAsync("/api/setup/esp32/probe", null));
        Assert.Equal("esp32_firmware_missing", probe.GetProperty("result").GetProperty("code").GetString());
        Assert.Equal("esp32_probe_esp32_firmware_missing", Check(probe.GetProperty("status"), "esp32_firmware"));

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/setup/esp32/flash", null)).StatusCode);
        var job = await WaitFlashAsync();
        Assert.Equal("done", job.GetProperty("job").GetProperty("stage").GetString());
        var status = job.GetProperty("status");
        Assert.Equal("esp32_firmware_ok", Check(status, "esp32_firmware"));
        Assert.Equal("esp32_chip_ok", Check(status, "esp32_chip"));
        Assert.Equal("esp32_firmware_verified", Check(status, "esp32_firmware_version"));

        // Teste com o Switch pelo ESP32 (simulado): o resultado vale para a porta e o chip.
        var keys = "aes_kek_generation_source = 00112233445566778899aabbccddeeff\naes_key_generation_source = 00112233445566778899aabbccddeeff\nmaster_key_00 = 00112233445566778899aabbccddeeff\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(keys)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
        var test = await Json(await _client.PostAsJsonAsync("/api/setup/esp32/switch-test", new { simulatedOutcome = "found_decrypted" }));
        Assert.Equal("esp32", test.GetProperty("result").GetProperty("radio").GetString());
        Assert.Equal("switch_test_ok", Check(test.GetProperty("status"), "switch_test"));

        // Placa escolhida diferente do chip: com o chip identificado, o aviso vem uma vez só (pelo chip).
        var other = await Json(await _client.PutAsJsonAsync("/api/setup/state", new { esp32Board = "esp32-c3" }));
        Assert.Equal("esp32_board_mismatch", Check(other, "esp32_chip"));
        Assert.Equal("esp32_firmware_ok", Check(other, "esp32_firmware"));
    }

    private async Task<object> TradeBodyAsync(int trades)
    {
        var list = await Json(await _client.GetAsync("/api/pokemon"));
        var party = list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToArray();
        return new { party, offeredSlots = Enumerable.Range(0, trades).Reverse().ToArray(), game = "fire_red", trainer = new { name = "ROCHA", tid = 12345, sid = 54321 } };
    }

    [Fact]
    public async Task Motor_esp32_passa_trocas_treinador_e_chaves_ao_nucleo()
    {
        var keys = "aes_kek_generation_source = 00112233445566778899aabbccddeeff\naes_key_generation_source = 00112233445566778899aabbccddeeff\nmaster_key_00 = 00112233445566778899aabbccddeeff\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(keys)), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", form));
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", serialPort = "/dev/ttyUSB0" }));

        var engine = _app.Services.GetServices<ITradeEngine>().Single(e => e.Id == "esp32");
        Assert.True((await engine.CheckAvailabilityAsync(CancellationToken.None)).Available);

        var events = new List<TradeEvent>();
        var pokemon = await Json(await _client.GetAsync("/api/pokemon"));
        var party = pokemon.EnumerateArray().Select(p => new PartyMember(p.GetProperty("id").GetGuid(), TradeRock.Core.Pokemon.DemoPokemon.All[0].Data, "x")).ToList();
        var request = new TradeRequest(party, [1, 0], TradeGame.FireRed, new TradeRock.Core.Trading.TrainerProfile("ROCHA", 12345, 54321));
        var result = await engine.RunAttemptAsync(request, e => { events.Add(e); return ValueTask.CompletedTask; }, CancellationToken.None);

        Assert.Equal(AttemptOutcome.Completed, result.Outcome);
        Assert.Equal(2, events.Count(e => e.Kind == TradeEventKind.TradeDone));
        var sent = _session.Last!;
        Assert.Equal("/dev/ttyUSB0", sent.Port);
        Assert.Equal(2, sent.TradeLimit);
        Assert.Equal(1, sent.Selected);
        Assert.Equal(12345u | (54321u << 16), sent.Profile.TrainerId);
        Assert.Equal(4, sent.Profile.GameVersion);
        Assert.EndsWith("prod.keys", sent.KeysPath);
        Assert.StartsWith(Path.Combine(_dataDir, "logs", "esp32"), sent.LogDir);
    }

    [Fact]
    public async Task Sala_nao_encontrada_e_cancelamento()
    {
        await Json(await _client.PutAsJsonAsync("/api/setup/state", new { radio = "esp32", serialPort = "/dev/ttyUSB0" }));
        var keysForm = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("aes_kek_generation_source = 00112233445566778899aabbccddeeff\naes_key_generation_source = 00112233445566778899aabbccddeeff\nmaster_key_00 = 00112233445566778899aabbccddeeff\n")), "file", "prod.keys" } };
        await Json(await _client.PostAsync("/api/setup/keys", keysForm));
        var party = new List<PartyMember> { new(Guid.NewGuid(), TradeRock.Core.Pokemon.DemoPokemon.All[0].Data, "a"), new(Guid.NewGuid(), TradeRock.Core.Pokemon.DemoPokemon.All[1].Data, "b") };
        var request = new TradeRequest(party, [1], TradeGame.LeafGreen, TradeRock.Core.Trading.TrainerProfile.Default);

        var noRoom = new Esp32SerialEngine(_app.Services.GetRequiredService<SetupService>(), _app.Services.GetRequiredService<ProdKeys>(),
            new FakeEsp32Session("no_room"), new Esp32EngineOptions { LogsDir = Path.Combine(_dataDir, "logs", "esp32") });
        var failed = await noRoom.RunAttemptAsync(request, _ => ValueTask.CompletedTask, CancellationToken.None);
        Assert.Equal("no_network_found", failed.Failure!.Code);
        Assert.True(failed.Failure.Retryable);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var hang = new Esp32SerialEngine(_app.Services.GetRequiredService<SetupService>(), _app.Services.GetRequiredService<ProdKeys>(),
            new FakeEsp32Session("hang"), new Esp32EngineOptions { LogsDir = Path.Combine(_dataDir, "logs", "esp32") });
        var result = await hang.RunAttemptAsync(request, _ => ValueTask.CompletedTask, cts.Token);
        Assert.Equal(AttemptOutcome.Cancelled, result.Outcome);
    }
}
