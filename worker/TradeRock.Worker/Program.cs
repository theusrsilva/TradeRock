// Worker privilegiado do TradeRock (só Linux, só para o motor com a placa Wi-Fi).
// Iniciado via pkexec com política polkit própria; fala com o app por um socket Unix
// com API mínima (iniciar troca, cancelar, status, testes de placa).
// Implementação na Entrega 6. Por enquanto só responde à versão.

if (args is ["--version"])
{
    Console.WriteLine(typeof(Program).Assembly.GetName().Version);
    return 0;
}

Console.Error.WriteLine("traderock-worker: ainda não implementado (Entrega 6).");
return 2;
