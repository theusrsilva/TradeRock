using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace TradeRock.Core.Engines.Worker;

/// <summary>Como iniciar o worker. O padrão é o pkexec; os testes usam um worker falso.</summary>
public interface IWorkerLauncher
{
    /// <summary>Instalação encontrada (null = worker não instalado).</summary>
    WorkerInstall? Find();

    Task<IWorkerConnection> LaunchAsync(WorkerInstall install, CancellationToken ct);
}

public sealed class WorkerOptions
{
    /// <summary>Em desenvolvimento a instalação dev (scripts/install-worker.sh dev) vem antes da release.</summary>
    public bool PreferDev { get; init; }

    /// <summary>Tempo para a pessoa digitar a senha no diálogo do sistema.</summary>
    public TimeSpan AuthTimeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>Inicia o launcher do worker via pkexec. A senha é pedida pelo polkit, nunca pelo app.</summary>
public sealed class PkexecWorkerLauncher(WorkerOptions options) : IWorkerLauncher
{
    public WorkerInstall? Find() => OperatingSystem.IsLinux() ? WorkerLocator.Find(preferDev: options.PreferDev) : null;

    public Task<IWorkerConnection> LaunchAsync(WorkerInstall install, CancellationToken ct)
    {
        var pkexec = WorkerLocator.PkexecPath() ?? throw new WorkerException("pkexec_missing", "pkexec não encontrado.");
        var start = new ProcessStartInfo(pkexec)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(install.Launcher);
        var process = Process.Start(start) ?? throw new WorkerException("worker_start_failed", "Não foi possível iniciar o pkexec.");
        return Task.FromResult<IWorkerConnection>(new ProcessConnection(process));
    }

    private sealed class ProcessConnection : IWorkerConnection
    {
        private readonly Process _process;

        public ProcessConnection(Process process)
        {
            _process = process;
            Exited = WaitExitAsync();
            // A saída de erro do pkexec/worker é drenada para não travar o processo (o worker manda o
            // log de diagnóstico pelo protocolo; aqui só sobram mensagens do próprio pkexec).
            _ = Task.Run(async () =>
            {
                try { await process.StandardError.ReadToEndAsync(); } catch (IOException) { }
            });
        }

        public Stream ToWorker => _process.StandardInput.BaseStream;
        public Stream FromWorker => _process.StandardOutput.BaseStream;
        public Task<int> Exited { get; }

        private async Task<int> WaitExitAsync()
        {
            await _process.WaitForExitAsync();
            return _process.ExitCode;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                // O worker roda como root: o app não consegue matá-lo. Fechar a entrada (feito pelo
                // WorkerClient) é o sinal para ele encerrar sozinho.
                await Task.WhenAny(Exited, Task.Delay(TimeSpan.FromSeconds(5)));
            }
            _process.Dispose();
        }
    }
}

/// <summary>
/// O worker da sessão: iniciado na primeira operação que precisar dele (a senha de administrador é
/// pedida uma vez) e mantido até o app fechar. Ao fechar, o worker devolve o rádio e sai.
/// </summary>
public sealed class WorkerHost(IWorkerLauncher launcher, WorkerOptions options, ILogger<WorkerHost> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private WorkerClient? _client;

    public WorkerInstall? Install => launcher.Find();

    /// <summary>Resposta do "hello" do worker em execução (versão, commit do motor, ferramentas).</summary>
    public JsonObject? Hello { get; private set; }

    public bool IsRunning => _client is { IsClosed: false };

    public async Task<WorkerClient> GetAsync(CancellationToken ct)
    {
        if (_client is { IsClosed: false } running)
            return running;

        await _startLock.WaitAsync(ct);
        try
        {
            if (_client is { IsClosed: false } again)
                return again;
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }

            var install = launcher.Find() ?? throw new WorkerException("worker_missing", "O componente de rede não está instalado.");
            if (!install.Safe)
                throw new WorkerException("worker_unsafe", "A instalação do componente de rede pode ser alterada sem ser administrador.");
            if (!install.PolicyInstalled)
                throw new WorkerException("worker_policy_missing", "A política do polkit do componente de rede não está instalada.");

            logger.LogInformation("Iniciando o worker ({Flavor}) via pkexec.", install.Flavor);
            var connection = await launcher.LaunchAsync(install, ct);
            var client = new WorkerClient(connection, logger);
            JsonObject hello;
            try
            {
                // A resposta só chega depois da senha: o pkexec espera o diálogo do polkit.
                var helloTask = client.RequestAsync("hello", null, options.AuthTimeout, ct);
                var first = await Task.WhenAny(helloTask, connection.Exited);
                if (first == connection.Exited)
                    throw PkexecFailure(await connection.Exited);
                hello = await helloTask;
            }
            catch (WorkerException e) when (e.Code == "worker_exited" && connection.Exited.IsCompleted)
            {
                await client.DisposeAsync();
                throw PkexecFailure(await connection.Exited);
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }

            if (hello["protocol"]?.GetValue<int>() != WorkerClient.Protocol)
            {
                await client.DisposeAsync();
                throw new WorkerException("worker_version", "O componente de rede instalado é de outra versão do TradeRock.");
            }
            if (hello["root"]?.GetValue<bool>() != true)
            {
                await client.DisposeAsync();
                throw new WorkerException("not_root", "O componente de rede não está rodando como administrador.");
            }

            logger.LogInformation("Worker pronto: motor {Commit}.", hello["engineCommit"]?.GetValue<string>());
            Hello = hello;
            _client = client;
            return client;
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>Códigos de saída do pkexec (man pkexec): 126 = diálogo fechado, 127 = sem autorização.</summary>
    private static WorkerException PkexecFailure(int exitCode) => exitCode switch
    {
        126 => new WorkerException("auth_cancelled", "A senha de administrador não foi informada."),
        127 => new WorkerException("not_authorized", "O sistema não autorizou o componente de rede."),
        _ => new WorkerException("worker_exited", $"O componente de rede fechou ao iniciar (código {exitCode})."),
    };

    public async Task<JsonObject> CardTestAsync(string phy, CancellationToken ct)
    {
        var client = await GetAsync(ct);
        return await client.RequestAsync("card_test", new JsonObject { ["phy"] = phy }, TimeSpan.FromSeconds(120), ct);
    }

    public async Task<JsonObject> InstallNetworkConfigAsync(CancellationToken ct)
    {
        var client = await GetAsync(ct);
        return await client.RequestAsync("net_install", null, TimeSpan.FromSeconds(60), ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _startLock.Dispose();
    }
}
