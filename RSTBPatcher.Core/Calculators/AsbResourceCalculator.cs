using BinaryReader = AeonSake.BinaryTools.BinaryReader;

namespace RSTBPatcher.Core.Calculators;

public class AsbResourceCalculator : IResourceCalculator
{
    public static uint CalculateSizeOffset(Stream stream, string romfsName)
    {
        // 1888 (before calculating) -> 2952 after calculated

        using var binaryReader = new BinaryReader(stream);
        var magic = binaryReader.ReadString(4);
        var version = binaryReader.ReadUInt16();
        //var test = binaryReader.ReadUInt32At(0xc);
        uint nodeCount = binaryReader.ReadUInt32At(0x14);
        uint test = binaryReader.ReadUInt32At(0x1c);
        uint test2 = binaryReader.ReadUInt32At(0x24);
        uint test3 = binaryReader.ReadUInt32At(0x2c);
        int exbOffset = binaryReader.ReadInt32At(0x74);
        uint size = 544 + 40 * nodeCount;

        if (exbOffset != 0)
        {
            int exbCountOffset = binaryReader.ReadInt32At(exbOffset + 0x20); // 0x20
            uint exbSignatureCount = binaryReader.ReadUInt32At(exbOffset + exbCountOffset);

            size += 16 + (exbSignatureCount + 1) / 2 * 8;
        }

        return size;
    }
}