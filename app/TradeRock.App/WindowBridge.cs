using System.Text.Json;
using Photino.NET;
using TradeRock.Core.Trading;

namespace TradeRock.App;

/// <summary>
/// Liga a janela nativa ao servidor. Fechar a janela durante uma troca não fecha na hora:
/// o frontend pergunta ao usuário e, se confirmado, chama /api/app/close, que cancela a troca
/// com segurança (encerra o motor e restaura o rádio) e só então fecha a janela.
/// </summary>
public sealed class WindowBridge(TradeCoordinator coordinator, ILogger<WindowBridge> logger)
{
    private PhotinoWindow? _window;
    private volatile bool _closeApproved;

    public bool HasWindow => _window is not null;

    public void Attach(PhotinoWindow window)
    {
        _window = window;
        window.RegisterWindowClosingHandler((_, _) =>
        {
            if (_closeApproved || !coordinator.IsBusy)
                return false; // pode fechar

            SendMessage(new { type = "close_requested" });
            return true; // segura a janela aberta até o usuário decidir
        });
    }

    /// <summary>Cancela a troca (se houver) e fecha a janela. Sem janela (--no-window), só cancela.</summary>
    public async Task CloseAsync()
    {
        await coordinator.CancelAsync(TimeSpan.FromSeconds(30));
        _closeApproved = true;
        if (_window is { } window)
        {
            logger.LogInformation("Fechando a janela a pedido do usuário.");
            window.Close();
        }
    }

    private void SendMessage(object message)
    {
        try
        {
            _window?.SendWebMessage(JsonSerializer.Serialize(message));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível avisar o frontend.");
        }
    }
}
