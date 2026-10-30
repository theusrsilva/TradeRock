using System.Text.Json.Serialization;
using TradeRock.Core.Games;

namespace TradeRock.Core.Trading;

/// <summary>
/// Versão do jogo do Switch. Gravada como texto no banco e em snake_case no JSON: novos valores só no fim.
/// O agrupamento por jogo (as duas versões de um par) está em <see cref="Games.GameCatalog"/>.
/// </summary>
public enum TradeGame
{
    FireRed,
    LeafGreen,
    LetsGoPikachu,
    LetsGoEevee,
    Sword,
    Shield,
    BrilliantDiamond,
    ShiningPearl,
    LegendsArceus,
    Scarlet,
    Violet,
    LegendsZA,
}

/// <summary>Treinador simulado que o Switch enxerga (nome no pedido de entrada, cartão, OT do lado do PC).</summary>
public sealed record TrainerProfile(string Name, ushort Tid, ushort? Sid)
{
    /// <summary>Padrão do frlg-ldn-trade (frlgsim/config.py DEFAULT_TRAINER).</summary>
    public static TrainerProfile Default { get; } = new("EMU", 0x8822, 0x47ED);
}

/// <summary>
/// Um Pokémon do time simulado (FRLG) ou uma oferta (outros jogos). <see cref="Data"/> são os bytes decriptados
/// guardados no cofre: o .pk3 (80 ou 100 bytes) ou a forma de caixa do formato do jogo.
/// </summary>
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
/// Pedido de troca. No FRLG, regras espelhadas do frlg-ldn-trade (frlgsim/config.py TradePlan) e do jogo:
/// - time de 2 a 6: o jogo não deixa o simulador trocar o último Pokémon do time, então sempre há um de preenchimento;
/// - de 1 a 6 trocas, cada uma oferecendo uma posição diferente do time.
/// Nos outros jogos (pokeldn, D21) não há time simulado: <see cref="Party"/> são as ofertas, uma por troca, de 1 a 6.
/// <see cref="LinkCode"/>: oito dígitos (vazio = sem código) ou, no Let's Go, três Pokémon da grade ("0,4,9").
/// </summary>
public sealed record TradeRequest(
    IReadOnlyList<PartyMember> Party,
    IReadOnlyList<int> OfferedSlots,
    TradeGame Game,
    TrainerProfile Trainer,
    SimulationOptions? Simulation = null,
    string? LinkCode = null)
{
    public const int MinParty = 2;
    public const int MaxParty = 6;

    public int Trades => OfferedSlots.Count;

    [JsonIgnore]
    public SwitchGame GameInfo => GameCatalog.Of(Game);

    /// <summary>Lista de problemas (códigos estáveis, traduzidos pelo frontend). Vazia = válido.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        var frlg = GameInfo.Family == GameFamily.Frlg;
        if (Party.Count < (frlg ? MinParty : 1) || Party.Count > MaxParty)
            errors.Add("party_size");

        if (OfferedSlots.Count is < 1 or > MaxParty)
            errors.Add("trades_count");
        else if (OfferedSlots.Count > Party.Count)
            errors.Add("trades_exceed_party");

        if (OfferedSlots.Distinct().Count() != OfferedSlots.Count)
            errors.Add("offered_slots_repeated");

        if (OfferedSlots.Any(s => s < 0 || s >= Party.Count))
            errors.Add("offered_slot_out_of_range");

        if (frlg && Party.Any(m => m.Data.Length is not (80 or 100)))
            errors.Add("party_data_size");

        if (!IsValidLinkCode(GameInfo.LinkCode, LinkCode))
            errors.Add("link_code_invalid");

        errors.AddRange(TrainerName.Validate(Trainer.Name, GameInfo));
        return errors;
    }

    public static bool IsValidLinkCode(LinkCodeKind kind, string? code) => kind switch
    {
        _ when string.IsNullOrEmpty(code) => true,
        LinkCodeKind.Digits8 => code.Length == 8 && code.All(char.IsAsciiDigit),
        LinkCodeKind.ThreePokemon => code.Split(',') is { Length: 3 } parts
            && parts.All(p => p.Length == 1 && char.IsAsciiDigit(p[0])),
        _ => false,
    };
}
