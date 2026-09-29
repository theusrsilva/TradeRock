namespace TradeRock.Core.Trading;

public enum TradeGame
{
    FireRed,
    LeafGreen,
}

/// <summary>Treinador simulado que o Switch enxerga (nome no pedido de entrada, cartão, OT do lado do PC).</summary>
public sealed record TrainerProfile(string Name, ushort Tid, ushort? Sid)
{
    /// <summary>Padrão do frlg-ldn-trade (frlgsim/config.py DEFAULT_TRAINER).</summary>
    public static TrainerProfile Default { get; } = new("EMU", 0x8822, 0x47ED);
}

/// <summary>Um Pokémon do time simulado. <see cref="Data"/> é o .pk3 decriptado (80 ou 100 bytes).</summary>
public sealed record PartyMember(Guid PokemonId, byte[] Data, string DisplayName);

/// <summary>Cenário do FakeEngine. Ignorado pelos motores reais.</summary>
public enum SimulationScenario
{
    Success,
    ConnectFailuresThenSuccess,
    NoNetworkFound,
    WrongKeys,
    ApprovalNeverGiven,
    JoinRejected,
    HostCancels,
    ConnectionLostMidTrade,
}

public sealed record SimulationOptions(SimulationScenario Scenario, int FailuresBeforeSuccess = 2);

/// <summary>
/// Pedido de troca. Regras espelhadas do frlg-ldn-trade (frlgsim/config.py TradePlan) e do jogo:
/// - time de 2 a 6: o jogo não deixa o simulador trocar o último Pokémon do time, então sempre há um de preenchimento;
/// - de 1 a 6 trocas, cada uma oferecendo uma posição diferente do time.
/// </summary>
public sealed record TradeRequest(
    IReadOnlyList<PartyMember> Party,
    IReadOnlyList<int> OfferedSlots,
    TradeGame Game,
    TrainerProfile Trainer,
    SimulationOptions? Simulation = null)
{
    public const int MinParty = 2;
    public const int MaxParty = 6;

    public int Trades => OfferedSlots.Count;

    /// <summary>Lista de problemas (códigos estáveis, traduzidos pelo frontend). Vazia = válido.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Party.Count is < MinParty or > MaxParty)
            errors.Add("party_size");

        if (OfferedSlots.Count is < 1 or > MaxParty)
            errors.Add("trades_count");
        else if (OfferedSlots.Count > Party.Count)
            errors.Add("trades_exceed_party");

        if (OfferedSlots.Distinct().Count() != OfferedSlots.Count)
            errors.Add("offered_slots_repeated");

        if (OfferedSlots.Any(s => s < 0 || s >= Party.Count))
            errors.Add("offered_slot_out_of_range");

        if (Party.Any(m => m.Data.Length is not (80 or 100)))
            errors.Add("party_data_size");

        errors.AddRange(TrainerName.Validate(Trainer.Name));
        return errors;
    }
}
