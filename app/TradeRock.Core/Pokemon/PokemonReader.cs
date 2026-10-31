using System.Security.Cryptography;
using PKHeX.Core;
using TradeRock.Core.Games;

namespace TradeRock.Core.Pokemon;

/// <summary>Resumo de um Pokémon de qualquer jogo do Switch para listas e cards.</summary>
public sealed record PokemonSummary(
    GameFamily Family,
    ushort Species,
    byte Form,
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
    bool Fateful,
    uint Pid);

/// <summary>Pokémon lido e normalizado: bytes decriptados, hash, resumo e o jogo do formato.</summary>
public sealed record PokemonFile(byte[] Data, string Sha256, PokemonSummary Summary)
{
    public SwitchGame Game => GameCatalog.Get(Summary.Family);
}

/// <summary>
/// Lê um Pokémon de qualquer jogo do catálogo (<see cref="GameCatalog"/>), decriptado ou criptografado.
/// - Gen 3: igual ao <see cref="Pk3Reader"/> (80 ou 100 bytes, hash dos bytes decriptados como vieram).
/// - Outros: guarda a forma "de caixa" decriptada (SIZE_STORED), para o mesmo Pokémon vindo de um arquivo de
///   equipe ou de caixa dar o mesmo hash. A forma de equipe é refeita pelo PKHeX quando precisa.
/// Sword/Shield e BDSP têm o mesmo tamanho de arquivo, assim como Scarlet/Violet e Legends: Z-A: nesses
/// casos o jogo vem da extensão do arquivo (.pk8/.pb8, .pk9/.pa9) ou de quem chamou (Pokémon recebido).
/// </summary>
public static class PokemonReader
{
    public const int MaxFileSize = 1024;

    public static PokemonFile Read(ReadOnlySpan<byte> file, GameFamily? family = null, string? fileName = null)
    {
        if (file.Length > MaxFileSize)
            throw new InvalidPokemonFileException("size", $"Arquivo com {file.Length} bytes: grande demais para um Pokémon.");

        var hinted = family ?? (fileName is null ? null : GameCatalog.ByExtension(Path.GetExtension(fileName))?.Family)
            ?? (Path.GetExtension(fileName ?? "") is ".ek3" ? GameFamily.Frlg : null);

        if (hinted is GameFamily.Frlg || (hinted is null && file.Length is Pk3Reader.StoredSize or Pk3Reader.PartySize))
            return FromPk3(Pk3Reader.Read(file));

        var pk = Parse(file, hinted);
        var game = GameCatalog.ByContext(pk.Context)
            ?? throw new InvalidPokemonFileException("game_unsupported",
                $"Pokémon de um jogo que o TradeRock não troca ({pk.GetType().Name}).");
        if (hinted is { } h && h != game.Family)
            throw new InvalidPokemonFileException("game_mismatch", $"O arquivo é de {game.Name}, não de {GameCatalog.Get(h).Name}.");
        if (!pk.ChecksumValid)
            throw new InvalidPokemonFileException("checksum", "O checksum do Pokémon não confere (arquivo corrompido?).");
        if (pk.Species is 0 || pk.Species > pk.MaxSpeciesID)
            throw new InvalidPokemonFileException("species", $"Espécie inválida ({pk.Species}).");

        var data = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(data);
        return new PokemonFile(data, Convert.ToHexStringLower(SHA256.HashData(data)), Summarize(pk, game.Family));
    }

    /// <summary>Monta o PKM do PKHeX a partir dos bytes guardados (decriptados) de um formato conhecido.</summary>
    public static PKM ToEntity(byte[] data, GameFamily family) =>
        family == GameFamily.Frlg ? new PK3(data.ToArray()) : Parse(data, family);

    public static PokemonSummary Summarize(PKM pk, GameFamily family)
    {
        // O PKHeX não tem nomes em português; os nomes em inglês são os mesmos usados no Brasil.
        var species = GameInfo.GetStrings("en").Species;
        var speciesName = pk.Species < species.Count ? species[pk.Species] : $"#{pk.Species}";
        return new PokemonSummary(
            Family: family,
            Species: pk.Species,
            Form: pk.Form,
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
            Fateful: pk.FatefulEncounter,
            Pid: pk.PID);
    }

    private static PokemonFile FromPk3(Pk3File pk3)
    {
        var pk = new PK3(pk3.Data.ToArray());
        return new PokemonFile(pk3.Data, pk3.Sha256, Summarize(pk, GameFamily.Frlg));
    }

    private static PKM Parse(ReadOnlySpan<byte> file, GameFamily? family)
    {
        // Com o jogo conhecido, monta direto o tipo do PKHeX: o EntityFormat confunde PB7 com PK7 e PA9 com PK9.
        if (family is { } known)
        {
            var type = GameCatalog.Get(known).EntityType;
            if (file.Length != Blank(type).SIZE_STORED && file.Length != Blank(type).SIZE_PARTY)
                throw new InvalidPokemonFileException("game_mismatch", $"Arquivo com {file.Length} bytes não é um Pokémon de {GameCatalog.Get(known).Name}.");
            return Create(known, file.ToArray());
        }

        return EntityFormat.GetFormat(file) switch
        {
            EntityFormatDetected.None => throw new InvalidPokemonFileException("size",
                $"Arquivo com {file.Length} bytes não é um Pokémon que o PKHeX reconheça."),
            EntityFormatDetected.FormatPK8 => new PK8(file.ToArray()),
            EntityFormatDetected.FormatPB8 => new PB8(file.ToArray()),
            EntityFormatDetected.FormatPA8 => new PA8(file.ToArray()),
            EntityFormatDetected.FormatPA9 => new PA9(file.ToArray()),
            EntityFormatDetected.FormatPB7 => new PB7(file.ToArray()),
            // Mesmo tamanho em dois jogos: PK8/PB8, PK9/PA9 (que o PKHeX também chama de PK9) e PK7/PB7.
            EntityFormatDetected.Format8or8b or EntityFormatDetected.Format9or9a or EntityFormatDetected.FormatPK9
                or EntityFormatDetected.Format6or7 or EntityFormatDetected.FormatPK7 => throw new InvalidPokemonFileException(
                "format_ambiguous",
                "Não dá para saber o jogo só pelo conteúdo: use a extensão do arquivo (.pb7, .pk8, .pb8, .pk9 ou .pa9)."),
            var other => EntityFormat.GetFromBytes(file.ToArray(), EntityContext.None)
                ?? throw new InvalidPokemonFileException("size", $"O PKHeX não reconheceu o arquivo como Pokémon ({other})."),
        };
    }

    private static PKM Create(GameFamily family, byte[] data) => family switch
    {
        GameFamily.Frlg => new PK3(data),
        GameFamily.Lgpe => new PB7(data),
        GameFamily.Swsh => new PK8(data),
        GameFamily.Bdsp => new PB8(data),
        GameFamily.Pla => new PA8(data),
        GameFamily.Sv => new PK9(data),
        GameFamily.Za => new PA9(data),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static readonly Dictionary<Type, PKM> Blanks = [];

    private static PKM Blank(Type type)
    {
        lock (Blanks)
        {
            if (!Blanks.TryGetValue(type, out var blank))
                Blanks[type] = blank = (PKM)Activator.CreateInstance(type)!;
            return blank;
        }
    }
}
