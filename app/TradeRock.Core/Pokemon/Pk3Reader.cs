using System.Security.Cryptography;
using PKHeX.Core;

namespace TradeRock.Core.Pokemon;

public sealed class InvalidPokemonFileException(string code, string message) : Exception(message)
{
    /// <summary>Código estável: "size", "checksum", "species".</summary>
    public string Code { get; } = code;
}

/// <summary>Resumo de um Pokémon da Gen 3 para listas e cards.</summary>
public sealed record Pk3Summary(
    ushort Species,
    string SpeciesName,
    string Nickname,
    byte Level,
    bool IsShiny,
    bool IsEgg,
    byte Gender,
    string OtName,
    ushort Tid,
    ushort Sid,
    string OriginGame,
    int Language,
    ushort HeldItem,
    bool Fateful)
{
    /// <summary>Espécie em que evolui ao ser trocado (ver <see cref="TradeEvolution"/>), ou null.</summary>
    public ushort? EvolvesOnTradeInto => TradeEvolution.EvolvesInto(Species, HeldItem);
}

/// <summary>.pk3 lido e normalizado: bytes decriptados (80 ou 100), hash e resumo.</summary>
public sealed record Pk3File(byte[] Data, string Sha256, Pk3Summary Summary)
{
    public bool HasPartyData => Data.Length == 100;
}

public static class Pk3Reader
{
    public const int StoredSize = 80;
    public const int PartySize = 100;
    public const ushort MaxSpecies = 386;

    /// <summary>
    /// Lê um .pk3 (decriptado) ou .ek3 (criptografado), de 80 (caixa) ou 100 bytes (time).
    /// O PK3 do PKHeX decripta sozinho quando detecta dados criptografados (PokeCrypto.DecryptIfEncrypted3).
    /// O hash é calculado sobre os bytes decriptados: o mesmo Pokémon em .pk3 e .ek3 tem o mesmo hash.
    /// </summary>
    public static Pk3File Read(ReadOnlySpan<byte> file)
    {
        if (file.Length is not (StoredSize or PartySize))
        {
            throw new InvalidPokemonFileException("size",
                $"Arquivo com {file.Length} bytes; um Pokémon da Gen 3 tem 80 ou 100 bytes.");
        }

        var pk = new PK3(file.ToArray());

        if (!pk.ChecksumValid)
            throw new InvalidPokemonFileException("checksum", "O checksum do Pokémon não confere (arquivo corrompido?).");

        if (pk.Species is 0 or > MaxSpecies)
            throw new InvalidPokemonFileException("species", $"Espécie inválida ({pk.Species}).");

        var data = new byte[file.Length];
        if (file.Length == PartySize)
            pk.WriteDecryptedDataParty(data);
        else
            pk.WriteDecryptedDataStored(data);

        return new Pk3File(data, Convert.ToHexStringLower(SHA256.HashData(data)), Summarize(pk));
    }

    private static Pk3Summary Summarize(PK3 pk)
    {
        // O PKHeX não tem nomes em português; os nomes em inglês são os mesmos usados no Brasil.
        var species = GameInfo.GetStrings("en").Species;
        var speciesName = pk.Species < species.Count ? species[pk.Species] : $"#{pk.Species}";

        return new Pk3Summary(
            Species: pk.Species,
            SpeciesName: speciesName,
            Nickname: pk.Nickname,
            Level: pk.CurrentLevel,
            IsShiny: pk.IsShiny,
            IsEgg: pk.IsEgg,
            Gender: pk.Gender,
            OtName: pk.OriginalTrainerName,
            Tid: pk.TID16,
            Sid: pk.SID16,
            OriginGame: pk.Version.ToString(),
            Language: pk.Language,
            HeldItem: (ushort)pk.HeldItem,
            Fateful: pk.FatefulEncounter);
    }
}
