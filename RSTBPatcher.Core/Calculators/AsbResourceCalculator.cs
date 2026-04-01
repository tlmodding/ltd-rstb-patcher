using System.Drawing;
using BinaryReader = AeonSake.BinaryTools.BinaryReader;

namespace RSTBPatcher.Core.Calculators;

public class AsbResourceCalculator : IResourceCalculator
{
    private static readonly Dictionary<ushort, int> nodeClassSizes = new()
    {
        [0x5] = 0x18,
        [0xE] = 0x28,
        [0xF] = 0x18,
    };

    public static uint CalculateSizeOffset(Stream stream, string romfsName)
    {
        var size = 0xe0 + 0x20 + 0x58 + 0x18 + 0x88 + 8 + 0x18 + 8;

        using var binaryReader = new BinaryReader(stream);

        var count = binaryReader.ReadInt32At(0x14);
        var offset = 0x80 + 0x30 * binaryReader.ReadInt32At(0x0C);

        for (int i = 0; i < count; i++)
        {
            var nodeClass = binaryReader.ReadUInt16At(offset);
            size += nodeClassSizes.TryGetValue(nodeClass, out int classSize) ? classSize + 8 : 0x20 + 8;
            offset += 0x24;
        }

        int exbOffset = binaryReader.ReadInt32At(0x74);

        if (exbOffset != 0)
        {
            int exbCountOffset = binaryReader.ReadInt32At(exbOffset + 0x20);
            count = binaryReader.ReadInt32At(exbOffset + exbCountOffset);
            size += 0x10 + 4 * ((count & 1) != 0 ? count + 1 : count);
        }

        return (uint) size;
    }
}