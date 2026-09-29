using System.Buffers.Binary;
using PKHeX.Core;

namespace TradeRock.Tests;

/// <summary>
/// Gera um .sav de FireRed/LeafGreen válido (128 KB) para os testes, sem depender de arquivo de fora.
/// O PKHeX só reconhece um save da Gen 3 se os 14 setores do bloco principal tiverem IDs 0..13 no
/// rodapé (SAV3.IsAllMainSectorsPresent) e se o bloco pequeno tiver 1 em 0xAC (marca de FRLG).
/// O save em branco do PKHeX não tem esses rodapés, então eles são montados aqui.
/// </summary>
internal static class SaveFixture
{
    private const int SectorSize = 0x1000;
    private const int SectorCount = 14;

    public static byte[] BlankFireRed(string ot = "RED", ushort tid = 24680, ushort sid = 13579)
    {
        var data = new byte[SaveUtil.SIZE_G3RAW];
        for (short id = 0; id < SectorCount; id++)
        {
            var sector = data.AsSpan(id * SectorSize, SectorSize);
            BinaryPrimitives.WriteInt16LittleEndian(sector[0xFF4..], id);
            BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFF8..], 0x08012025); // assinatura do setor
            BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFFC..], 1); // contador de gravações
        }
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xAC), 1); // FRLG
        // Jogos internacionais preenchem o fim do nome (bytes 6-7 do bloco pequeno) com 0xFF;
        // zeros ali fazem o PKHeX tratar o save como japonês.
        data[0x6] = 0xFF;
        data[0x7] = 0xFF;

        var sav = new SAV3FRLG(data) { OT = ot, TID16 = tid, SID16 = sid };
        return sav.Write().ToArray();
    }

    public static SaveFile Open(byte[] bytes) =>
        SaveUtil.GetSaveFile(bytes) ?? throw new InvalidOperationException("O PKHeX não reconheceu o save gerado.");
}

public sealed class SaveFixtureTests
{
    [Fact]
    public void O_pkhex_reconhece_o_save_gerado()
    {
        var sav = SaveFixture.Open(SaveFixture.BlankFireRed());
        Assert.IsType<SAV3FRLG>(sav);
        Assert.Equal("RED", sav.OT);
        Assert.Equal(14, sav.BoxCount);
    }
}
