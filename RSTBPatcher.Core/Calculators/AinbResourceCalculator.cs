using BinaryReader = AeonSake.BinaryTools.BinaryReader;

namespace RSTBPatcher.Core.Calculators;

public class AinbResourceCalculator : IResourceCalculator
{
    public static uint CalculateSizeOffset(Stream stream, string romfsName)
    {
        uint size = 392;

        using var binaryReader = new BinaryReader(stream);
        int exbOffset = binaryReader.ReadInt32At(0x44);

        if (exbOffset != 0)
        {
            int exbCountOffset = binaryReader.ReadInt32At(exbOffset + 0x20);
            uint exbSignatureCount = binaryReader.ReadUInt32At(exbOffset + exbCountOffset);

            size += 16 + (exbSignatureCount + 1) / 2 * 8;
        }

        return size;
    }
}