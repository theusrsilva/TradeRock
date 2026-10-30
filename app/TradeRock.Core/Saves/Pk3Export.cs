using PKHeX.Core;
using TradeRock.Core.Games;
using TradeRock.Core.Pokemon;

namespace TradeRock.Core.Saves;

/// <summary>Exporta um Pokémon guardado (decriptado) como .pk3 ou .ek3, com 100 (time) ou 80 bytes (caixa).</summary>
public static class Pk3Export
{
    public static IReadOnlyList<string> Formats { get; } = ["pk3", "ek3"];

    public static byte[] ToFile(byte[] decrypted, string format, int size)
    {
        if (!Formats.Contains(format))
            throw new ArgumentException("Formato inválido.", nameof(format));
        if (size is not (Pk3Reader.StoredSize or Pk3Reader.PartySize))
            throw new ArgumentException("Tamanho inválido.", nameof(size));

        var pk = new PK3(decrypted.ToArray());
        if (size == Pk3Reader.PartySize && !pk.PartyStatsPresent)
            pk.ResetPartyStats();

        var result = new byte[size];
        switch (format, size)
        {
            case ("pk3", Pk3Reader.PartySize): pk.WriteDecryptedDataParty(result); break;
            case ("pk3", _): pk.WriteDecryptedDataStored(result); break;
            case ("ek3", Pk3Reader.PartySize): pk.WriteEncryptedDataParty(result); break;
            default: pk.WriteEncryptedDataStored(result); break;
        }
        return result;
    }

    public static string FileName(ushort species, string nickname, string format) =>
        $"{species:000}-{Sanitize(nickname)}.{format}";

    private static string Sanitize(string name)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length > 0 ? clean : "pokemon";
    }
}

/// <summary>
/// Exportação de qualquer jogo. FireRed/LeafGreen seguem o <see cref="Pk3Export"/> (.pk3/.ek3, 80 ou 100 bytes).
/// Os outros saem no formato do próprio jogo (.pb7, .pk8, .pb8, .pa8, .pk9, .pa9), decriptados, como o PKHeX
/// abre: "ek3" e o tamanho escolhidos para a Gen 3 não se aplicam (a forma de caixa é a guardada).
/// </summary>
public static class PokemonExport
{
    public static (byte[] Data, string Extension) ToFile(byte[] stored, GameFamily family, string format, int size)
    {
        if (family == GameFamily.Frlg)
            return (Pk3Export.ToFile(stored, format, size), format);
        var pk = PokemonReader.ToEntity(stored, family);
        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return (data, GameCatalog.Get(family).Extension);
    }
}
