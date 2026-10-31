using PKHeX.Core;
using TradeRock.Core.Games;
using TradeRock.Core.Pokemon;

namespace TradeRock.Core.Engines.Pokeldn;

/// <summary>
/// Uma oferta no formato que o pokeldn recebe de cada jogo: o mesmo que o ajudante PKHeX dele grava
/// (engines/pokeldn/services/pkhex/Program.cs, tabela de jogos: Write de cada um).
/// </summary>
public static class PokeldnOffer
{
    public static byte[] Bytes(byte[] stored, GameFamily family)
    {
        var pk = PokemonReader.ToEntity(stored, family);
        if (pk is not PK3 { PartyStatsPresent: true })
            pk.ResetPartyStats();
        pk.RefreshChecksum();
        return family switch
        {
            GameFamily.Frlg or GameFamily.Za => Write(pk.SIZE_PARTY, pk.WriteDecryptedDataParty),
            GameFamily.Bdsp => Write(pk.SIZE_STORED, pk.WriteEncryptedDataStored),
            _ => Write(pk.SIZE_PARTY, pk.WriteEncryptedDataParty), // Let's Go, SwSh, Arceus, SV
        };
    }

    private delegate void Writer(Span<byte> destination);

    private static byte[] Write(int size, Writer write)
    {
        var data = new byte[size];
        write(data);
        return data;
    }

    /// <summary>Chave do jogo no pokeldn (a mesma do catálogo do TradeRock).</summary>
    public static string GameKey(GameFamily family) => GameCatalog.Get(family).Key;
}
