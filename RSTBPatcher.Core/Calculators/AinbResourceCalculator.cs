using BinaryReader = AeonSake.BinaryTools.BinaryReader;

namespace RSTBPatcher.Core.Calculators;

public class AinbResourceCalculator : IResourceCalculator
{
    public static uint CalculateSizeOffset(Stream stream, string romfsName)
    {
        uint size = 0xe0; // sead::FrameHeap
        size += 0x20; // sead::MemBlock
        size += 0x30; // AIResource
        size += 0x10; // Unknown
        size += 0x68; // Extractor

        using var binaryReader = new BinaryReader(stream);
        var exbOffset = binaryReader.ReadInt32At(0x44);

        if (exbOffset != 0)
        {
            var exbCountOffset = binaryReader.ReadInt32At(exbOffset + 0x20);
            var count = binaryReader.ReadUInt32At(exbOffset + exbCountOffset);

            size += 16 + 4 * ((count & 1) != 0 ? count + 1 : count);
        }

        return size;
    }
}