namespace TradeRock.Core.Pokemon;

/// <summary>
/// Evolução por troca na Gen 3, conforme pret/pokefirered:
/// src/data/pokemon/evolution.h (EVO_TRADE / EVO_TRADE_ITEM) e GetEvolutionTargetSpecies em
/// src/pokemon.c (a Everstone impede). IDs de item de include/constants/items.h.
///
/// Importa porque o Switch trava ao salvar depois de uma evolução por troca recebida do
/// frlg-ldn-trade (issue #8 do tornadus/frlg-ldn-trade). O assistente avisa antes de enviar.
/// </summary>
public static class TradeEvolution
{
    private const ushort KingsRock = 187;
    private const ushort DeepSeaTooth = 192;
    private const ushort DeepSeaScale = 193;
    private const ushort Everstone = 195;
    private const ushort MetalCoat = 199;
    private const ushort DragonScale = 201;
    private const ushort UpGrade = 218;

    // EVO_TRADE: evolui sempre.
    private static readonly Dictionary<ushort, ushort> Always = new()
    {
        [64] = 65,   // Kadabra → Alakazam
        [67] = 68,   // Machoke → Machamp
        [75] = 76,   // Graveler → Golem
        [93] = 94,   // Haunter → Gengar
    };

    // EVO_TRADE_ITEM: evolui segurando o item.
    private static readonly Dictionary<(ushort Species, ushort Item), ushort> WithItem = new()
    {
        [(61, KingsRock)] = 186,     // Poliwhirl → Politoed
        [(79, KingsRock)] = 199,     // Slowpoke → Slowking
        [(95, MetalCoat)] = 208,     // Onix → Steelix
        [(117, DragonScale)] = 230,  // Seadra → Kingdra
        [(123, MetalCoat)] = 212,    // Scyther → Scizor
        [(137, UpGrade)] = 233,      // Porygon → Porygon2
        [(366, DeepSeaTooth)] = 367, // Clamperl → Huntail
        [(366, DeepSeaScale)] = 368, // Clamperl → Gorebyss
    };

    /// <summary>Espécie em que evolui ao ser trocado, ou null.</summary>
    public static ushort? EvolvesInto(ushort species, ushort heldItem)
    {
        if (heldItem == Everstone)
            return null;
        if (Always.TryGetValue(species, out var target))
            return target;
        return WithItem.TryGetValue((species, heldItem), out target) ? target : null;
    }
}
