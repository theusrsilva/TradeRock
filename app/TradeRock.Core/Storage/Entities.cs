using TradeRock.Core.Trading;

namespace TradeRock.Core.Storage;

public enum PokemonOrigin
{
    Imported,
    Received,
    Demo,
}

/// <summary>
/// Pokémon guardado. O arquivo (.pk3 decriptado) fica em disco, nomeado pelo SHA-256;
/// aqui ficam o hash e o resumo para listar e buscar. A Entrega 4 (cofre) amplia esta tabela.
/// </summary>
public sealed class StoredPokemon
{
    public Guid Id { get; set; }
    public required string Sha256 { get; set; }
    public int Size { get; set; }
    public ushort Species { get; set; }
    public required string SpeciesName { get; set; }
    public required string Nickname { get; set; }
    public byte Level { get; set; }
    public bool IsShiny { get; set; }
    public bool IsEgg { get; set; }
    public byte Gender { get; set; }
    public required string OtName { get; set; }
    public ushort Tid { get; set; }
    public ushort Sid { get; set; }
    public required string OriginGame { get; set; }

    /// <summary>Item segurado (ID interno da Gen 3).</summary>
    public ushort HeldItem { get; set; }

    public PokemonOrigin Origin { get; set; }
    public string? SourceFileName { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>Histórico de uma sessão de troca (uma ou mais trocas com o mesmo Switch).</summary>
public sealed class TradeRecord
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public required string EngineId { get; set; }
    public bool IsSimulation { get; set; }
    public TradeGame Game { get; set; }
    public required string TrainerName { get; set; }
    public ushort Tid { get; set; }
    public ushort? Sid { get; set; }
    public TradeSessionStatus Status { get; set; }
    public int Attempts { get; set; }
    public int TradesPlanned { get; set; }
    public int TradesCompleted { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    /// <summary>Eventos da sessão em JSON (sem dados de chave: os motores nunca os emitem).</summary>
    public required string EventsJson { get; set; }

    public List<TradeRecordEntry> Entries { get; set; } = [];
}

public sealed class TradeRecordEntry
{
    public int Id { get; set; }
    public Guid TradeRecordId { get; set; }
    public int TradeIndex { get; set; }
    public Guid SentPokemonId { get; set; }
    public Guid? ReceivedPokemonId { get; set; }
}

/// <summary>Treinador simulado salvo (preset).</summary>
public sealed class TrainerPreset
{
    public Guid Id { get; set; }
    public required string Label { get; set; }
    public required string Name { get; set; }
    public ushort Tid { get; set; }
    public ushort? Sid { get; set; }
    public TradeGame Game { get; set; }
}

/// <summary>Configuração chave → JSON.</summary>
public sealed class SettingEntry
{
    public required string Key { get; set; }
    public required string Json { get; set; }
}
