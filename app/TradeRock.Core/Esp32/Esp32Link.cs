namespace TradeRock.Core.Esp32;

/// <summary>Erro do ESP32. <see cref="Code"/> é estável (traduzido no frontend).</summary>
public sealed class Esp32Exception(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Resposta do firmware ao HELLO: o chip (esp32, esp32c3, esp32c6, esp32s3).</summary>
public sealed record Esp32Hello(string Target);

/// <summary>Uma sala vista pelo ESP32 no teste com o Switch.</summary>
public sealed record Esp32Room(ulong CommunicationId, int Channel, int Protocol, int Members, int Maximum, int Policy);

public sealed record Esp32ScanResult(IReadOnlyList<Esp32Room> Rooms, int Undecryptable, IReadOnlyList<int> UndecryptableChannels,
    int Sweeps, double Seconds, bool FoundFrlg);

/// <summary>Conversa com o firmware pela porta serial, sem entrar em sala nenhuma.</summary>
public interface IEsp32Link
{
    /// <summary>HELLO do firmware do pokeldn: há firmware compatível e a comunicação funciona?</summary>
    Esp32Hello Probe(string port, CancellationToken ct);

    /// <summary>Escuta as salas dos jogos nos canais de 2,4 GHz e tenta decifrar os anúncios com o prod.keys.</summary>
    Esp32ScanResult Scan(string port, string keysPath, TimeSpan limit, CancellationToken ct);
}
