using System.Security.Cryptography;
using PKHeX.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Pokemon;

namespace TradeRock.Core.Saves;

public sealed record SaveSlot(int Box, int Slot, ushort Species, string SpeciesName, string Nickname, byte Level, bool IsEgg);

public sealed record SaveLayout(
    string Game,
    string TrainerName,
    ushort Tid,
    ushort Sid,
    int BoxCount,
    int SlotsPerBox,
    IReadOnlyList<string> BoxNames,
    IReadOnlyList<SaveSlot> Occupied);

/// <summary>Um Pokémon (decriptado) indo para uma posição de caixa do save.</summary>
public sealed record Placement(byte[] Data, int Box, int Slot);

public sealed class SaveInjectionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Injeção de Pokémon em saves de GBA (Gen 3) pelo PKHeX.Core, que refaz os checksums e preserva
/// cabeçalho/rodapé de emulador (RTC, Nintendo Switch Online). Só usa posições vazias.
/// </summary>
public static class SaveInjection
{
    // Não mexe no Pokémon; atualiza a Pokédex do save; não mexe nos registros.
    private static readonly EntityImportSettings Settings =
        new(EntityImportOption.Disable, EntityImportOption.Enable, EntityImportOption.Disable);

    public static SaveFile Open(byte[] file)
    {
        if (file.Length > SaveImport.MaxSaveBytes)
            throw new SaveInjectionException("sav_size", "Arquivo grande demais para um save de GBA.");
        var sav = SaveUtil.GetSaveFile(file.ToArray()) ?? throw new SaveInjectionException("sav_unknown", "Não é um save reconhecido.");
        if (sav is not SAV3)
            throw new SaveInjectionException("sav_not_gen3", "O save não é da terceira geração (GBA).");
        return sav;
    }

    public static SaveLayout Describe(byte[] file)
    {
        var sav = Open(file);
        var names = Enumerable.Range(0, sav.BoxCount)
            .Select(i => sav is IBoxDetailName n && !string.IsNullOrWhiteSpace(n.GetBoxName(i)) ? n.GetBoxName(i) : $"Box {i + 1}")
            .ToList();
        var occupied = new List<SaveSlot>();
        for (var box = 0; box < sav.BoxCount; box++)
        {
            var data = sav.GetBoxData(box);
            for (var slot = 0; slot < data.Length; slot++)
            {
                var pk = data[slot];
                if (pk.Species == 0)
                    continue;
                occupied.Add(new SaveSlot(box, slot, pk.Species, BuilderData.Strings.specieslist[pk.Species], pk.Nickname, pk.CurrentLevel, pk.IsEgg));
            }
        }
        return new SaveLayout(sav.Version.ToString(), sav.OT, sav.TID16, sav.SID16, sav.BoxCount, sav.BoxSlotCount, names, occupied);
    }

    /// <summary>
    /// Coloca os Pokémon nas posições pedidas e devolve o save gravado. Depois de gravar, relê o
    /// resultado pelo PKHeX e confere cada posição (espécie, PID e checksum).
    /// </summary>
    public static byte[] Inject(byte[] file, IReadOnlyList<Placement> placements)
    {
        if (placements.Count == 0)
            throw new SaveInjectionException("nothing_to_inject", "Nenhum Pokémon escolhido.");

        var sav = Open(file);
        if (placements.GroupBy(p => (p.Box, p.Slot)).Any(g => g.Count() > 1))
            throw new SaveInjectionException("slot_repeated", "Duas escolhas para a mesma posição.");

        var expected = new List<(int Box, int Slot, ushort Species, uint Pid)>();
        foreach (var p in placements)
        {
            if (p.Box < 0 || p.Box >= sav.BoxCount || p.Slot < 0 || p.Slot >= sav.BoxSlotCount)
                throw new SaveInjectionException("slot_invalid", $"Posição inválida: caixa {p.Box + 1}, posição {p.Slot + 1}.");
            if (sav.GetBoxSlotAtIndex(p.Box, p.Slot).Species != 0)
                throw new SaveInjectionException("slot_occupied", $"A posição {p.Slot + 1} da caixa {p.Box + 1} não está vazia.");

            var pk = new PK3(p.Data.ToArray());
            sav.SetBoxSlotAtIndex(pk, p.Box, p.Slot, Settings);
            expected.Add((p.Box, p.Slot, pk.Species, pk.PID));
        }

        var result = sav.Write().ToArray();

        var check = Open(result);
        foreach (var (box, slot, species, pid) in expected)
        {
            var pk = check.GetBoxSlotAtIndex(box, slot);
            if (pk.Species != species || pk.PID != pid || !pk.ChecksumValid)
                throw new SaveInjectionException("verify_failed", "A conferência do save gravado falhou; nada foi alterado.");
        }
        return result;
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}

/// <summary>Cópias de segurança dos saves antes de qualquer alteração (pasta de dados, backups/).</summary>
public sealed class SaveBackups(AppPaths paths, TimeProvider time)
{
    public string Directory => Path.Combine(paths.DataDir, "backups");

    public string Create(byte[] original, string originalName)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var stamp = time.GetUtcNow().ToLocalTime().ToString("yyyyMMdd-HHmmss");
        var name = Path.GetFileNameWithoutExtension(originalName);
        var safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').ToArray()).Trim();
        if (safe.Length == 0)
            safe = "save";
        var path = Path.Combine(Directory, $"{stamp}-{safe}-{SaveInjection.Sha256(original)[..8]}{Path.GetExtension(originalName)}");
        File.WriteAllBytes(path, original);
        return path;
    }
}
