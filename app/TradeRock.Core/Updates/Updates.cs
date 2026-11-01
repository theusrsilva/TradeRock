using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeRock.Core.Activities;
using TradeRock.Core.Storage;
using TradeRock.Core.Trading;

namespace TradeRock.Core.Updates;

/// <summary>Como este TradeRock foi instalado, e portanto qual arquivo do Release o atualiza (D29).</summary>
public enum InstallKind
{
    /// <summary>Rodando do código ou de uma pasta solta: só avisa.</summary>
    None,
    WindowsInstaller,
    AppImage,
    Deb,
}

public sealed record ReleaseAsset(string Name, string Url, long Size);

public sealed record ReleaseInfo(string Version, string Tag, string Url, string Notes, DateTimeOffset? PublishedAt, IReadOnlyList<ReleaseAsset> Assets);

/// <param name="Stage">idle | checking | downloading | verifying | installing | restarting | error</param>
public sealed record UpdateStatus(
    string Current,
    InstallKind Kind,
    bool AutoCheck,
    DateTimeOffset? CheckedAt,
    ReleaseInfo? Latest,
    bool Available,
    string Stage,
    long Done = 0,
    long Total = 0,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed class UpdateException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Versões do TradeRock: números separados por ponto ("01.00.00", "0.8.1"); um sufixo com "-" é pré-lançamento.</summary>
public static class AppVersions
{
    public static int[]? Parse(string text)
    {
        var core = text.Trim().TrimStart('v', 'V').Split('+')[0];
        if (core.Contains('-'))
            return null; // pré-lançamento: fora das atualizações (decisão do autor)
        var parts = core.Split('.');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
                return null;
        return numbers;
    }

    /// <summary><paramref name="candidate"/> é mais nova que <paramref name="current"/> (os dois sem pré-lançamento).</summary>
    public static bool IsNewer(string candidate, string current)
    {
        if (Parse(candidate) is not { } a || Parse(current) is not { } b)
            return false;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y)
                return x > y;
        }
        return false;
    }

    /// <summary>O arquivo do Release para cada tipo de instalação (nomes de scripts/package-*.sh).</summary>
    public static string? AssetName(InstallKind kind, string version) => kind switch
    {
        InstallKind.WindowsInstaller => $"TradeRock-Setup-{version}.exe",
        InstallKind.AppImage => $"TradeRock-{version}-x86_64.AppImage",
        InstallKind.Deb => $"traderock_{version.Replace('-', '~')}_amd64.deb",
        _ => null,
    };

    /// <summary>"&lt;sha256&gt;  &lt;arquivo&gt;" por linha (sha256sum).</summary>
    public static string? HashOf(string sums, string file) =>
        sums.Split('\n')
            .Select(l => l.Trim().Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].TrimStart('*') == file && p[0].Length == 64 && p[0].All(Uri.IsHexDigit))
            .Select(p => p[0].ToLowerInvariant())
            .FirstOrDefault();
}

/// <summary>Instala o arquivo baixado e reabre o app. Os testes usam um falso.</summary>
public interface IUpdateInstaller
{
    InstallKind Kind { get; }

    /// <summary>Instala <paramref name="file"/> (já conferido com <paramref name="sha256"/>) e agenda o app novo.</summary>
    Task InstallAsync(string file, string sha256, CancellationToken ct);
}

/// <summary>Fecha este app depois da instalação (a janela, ou o servidor sem janela).</summary>
public interface IAppExit
{
    Task ExitAsync();
}

/// <summary>
/// Atualização pela internet (D29): consulta o último Release do GitHub, baixa o arquivo do tipo de instalação,
/// confere com o SHA256SUMS do mesmo Release e instala. Só fala com o GitHub; nada do PC é enviado.
/// </summary>
public sealed class UpdateService(
    HttpClient http,
    SettingsStore settings,
    IUpdateInstaller installer,
    IAppExit exit,
    TradeCoordinator trades,
    ActivityService activities,
    TimeProvider time,
    ILogger<UpdateService> logger,
    UpdateOptions options)
{
    public const string AutoKey = "update.auto";
    public const string CheckedKey = "update.checkedAt";
    public static readonly TimeSpan AutoInterval = TimeSpan.FromDays(1);

    private readonly Lock _lock = new();
    private ReleaseInfo? _latest;
    private DateTimeOffset? _checkedAt;
    private string _stage = "idle";
    private long _done, _total;
    private string? _errorCode, _errorMessage;
    private bool _running;

    public async Task<UpdateStatus> StatusAsync(CancellationToken ct)
    {
        var auto = await settings.GetAsync(AutoKey, true, ct);
        _checkedAt ??= await settings.GetAsync<DateTimeOffset?>(CheckedKey, null, ct);
        lock (_lock)
        {
            var available = _latest is not null && AppVersions.IsNewer(_latest.Version, options.CurrentVersion);
            return new UpdateStatus(options.CurrentVersion, installer.Kind, auto, _checkedAt, _latest, available, _stage, _done, _total,
                _errorCode, _errorMessage);
        }
    }

    public Task SetAutoAsync(bool on, CancellationToken ct) => settings.SetAsync(AutoKey, on, ct);

    /// <summary>A procura automática ao abrir: só ligada e no máximo uma vez por dia.</summary>
    public async Task AutoCheckAsync(CancellationToken ct)
    {
        if (!await settings.GetAsync(AutoKey, true, ct))
            return;
        var last = await settings.GetAsync<DateTimeOffset?>(CheckedKey, null, ct);
        if (last is { } at && time.GetUtcNow() - at < AutoInterval)
        {
            // Já procurou hoje: mostra o que achou da última vez, sem consultar de novo.
            if (await settings.GetAsync<ReleaseInfo?>(LatestKey, null, ct) is { } saved)
                lock (_lock)
                    _latest ??= saved;
            return;
        }
        try
        {
            await CheckAsync(ct);
        }
        catch (UpdateException)
        {
            // A procura automática falha em silêncio (sem internet); o erro fica no status.
        }
    }

    private const string LatestKey = "update.latest";

    public async Task<UpdateStatus> CheckAsync(CancellationToken ct)
    {
        SetStage("checking");
        try
        {
            using var response = await http.GetAsync($"https://api.github.com/repos/{options.Repository}/releases/latest", ct);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException("update_check_failed", $"GitHub respondeu {(int)response.StatusCode}.");
            var release = Parse(await response.Content.ReadAsStringAsync(ct), options.Repository);
            var now = time.GetUtcNow();
            await settings.SetAsync(CheckedKey, now, ct);
            await settings.SetAsync(LatestKey, release, ct);
            lock (_lock)
            {
                _latest = release;
                _checkedAt = now;
            }
            logger.LogInformation("Atualização: a última versão é {Latest} (esta é {Current}).", release?.Version ?? "?", options.CurrentVersion);
            SetStage("idle");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or UpdateException && !ct.IsCancellationRequested)
        {
            var code = e is UpdateException u ? u.Code : "update_check_failed";
            logger.LogWarning("Atualização: a procura falhou ({Code}). {Message}", code, e.Message);
            SetStage("error", code, e.Message);
            throw new UpdateException(code, e.Message);
        }
        return await StatusAsync(ct);
    }

    /// <summary>Baixa, confere e instala a versão nova em segundo plano; a tela acompanha pelo status.</summary>
    public async Task<UpdateStatus> StartInstallAsync(CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (!status.Available || status.Latest is not { } release)
            throw new UpdateException("update_none", "Não há versão nova.");
        if (installer.Kind == InstallKind.None || AppVersions.AssetName(installer.Kind, release.Version) is not { } name)
            throw new UpdateException("update_manual", "Esta instalação não se atualiza sozinha.");
        if (trades.IsBusy || activities.IsBusy)
            throw new UpdateException("busy", "Há uma troca ou atividade em andamento.");
        if (release.Assets.FirstOrDefault(a => a.Name == name) is not { } asset
            || release.Assets.FirstOrDefault(a => a.Name == "SHA256SUMS") is not { } sums)
            throw new UpdateException("update_asset_missing", $"O Release não tem {name} ou o SHA256SUMS.");
        lock (_lock)
        {
            if (_running)
                throw new UpdateException("busy", "A atualização já está em andamento.");
            _running = true;
        }
        SetStage("downloading");
        _ = Task.Run(() => InstallAsync(asset, sums));
        return await StatusAsync(ct);
    }

    private async Task InstallAsync(ReleaseAsset asset, ReleaseAsset sumsAsset)
    {
        var dir = Path.Combine(options.DownloadDir, "update");
        var file = Path.Combine(dir, asset.Name);
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateFiles(dir))
                File.Delete(old);
            var sums = await http.GetStringAsync(sumsAsset.Url);
            var expected = AppVersions.HashOf(sums, asset.Name)
                ?? throw new UpdateException("update_asset_missing", $"O SHA256SUMS não tem {asset.Name}.");

            using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? asset.Size;
                lock (_lock)
                    (_done, _total) = (0, total);
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(file);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    lock (_lock)
                        _done += read;
                }
            }

            SetStage("verifying");
            string actual;
            await using (var stream = File.OpenRead(file))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            if (actual != expected)
                throw new UpdateException("update_hash_mismatch", "O arquivo baixado não confere com o SHA256SUMS do Release.");

            SetStage("installing");
            logger.LogInformation("Atualização: instalando {File}.", asset.Name);
            await installer.InstallAsync(file, expected, CancellationToken.None);
            SetStage("restarting");
            await exit.ExitAsync();
        }
        catch (Exception e)
        {
            var code = e is UpdateException u ? u.Code : e is HttpRequestException ? "update_download_failed" : "update_failed";
            logger.LogWarning(e, "Atualização: falhou ({Code}).", code);
            SetStage("error", code, e.Message);
            try { File.Delete(file); } catch (IOException) { }
        }
        finally
        {
            lock (_lock)
                _running = false;
        }
    }

    private void SetStage(string stage, string? code = null, string? message = null)
    {
        lock (_lock)
        {
            _stage = stage;
            _errorCode = code;
            _errorMessage = message;
            if (stage is "checking" or "downloading")
                (_done, _total) = (0, 0);
        }
    }

    /// <summary>A resposta de /releases/latest. Só os arquivos do próprio Release, por https no github.com.</summary>
    public static ReleaseInfo? Parse(string json, string repository)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = tag.TrimStart('v', 'V');
        if (AppVersions.Parse(version) is null)
            return null;
        var prefix = $"https://github.com/{repository}/releases/download/{tag}/";
        var assets = root.TryGetProperty("assets", out var list)
            ? list.EnumerateArray()
                .Select(a => new ReleaseAsset(a.GetProperty("name").GetString() ?? "", a.GetProperty("browser_download_url").GetString() ?? "",
                    a.TryGetProperty("size", out var size) ? size.GetInt64() : 0))
                .Where(a => a.Url == prefix + Uri.EscapeDataString(a.Name))
                .ToList()
            : [];
        var url = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";
        if (!url.StartsWith($"https://github.com/{repository}/releases/", StringComparison.Ordinal))
            url = $"https://github.com/{repository}/releases";
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        DateTimeOffset? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : null;
        return new ReleaseInfo(version, tag, url, notes, published, assets);
    }

    /// <summary>O HttpClient das atualizações: User-Agent (o GitHub exige) e tempo limite.</summary>
    public static void Configure(HttpClient http, string version)
    {
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TradeRock", version.Split('+')[0]));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.Timeout = TimeSpan.FromMinutes(10);
    }
}

public sealed class UpdateOptions
{
    public string Repository { get; init; } = "theusrsilva/TradeRock";
    public required string CurrentVersion { get; init; }
    public required string DownloadDir { get; init; }
}
