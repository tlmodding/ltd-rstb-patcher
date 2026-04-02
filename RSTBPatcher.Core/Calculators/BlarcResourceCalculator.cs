using AeonSake.NintendoTools.FileFormats.Sarc;

using BinaryReader = AeonSake.BinaryTools.BinaryReader;

namespace RSTBPatcher.Core.Calculators;

public class BlarcResourceCalculator : IResourceCalculator
{
    public static SarcFileReader SarcFileReader { get; } = new SarcFileReader();

    public static uint CalculateSizeOffset(Stream stream, string romfsName)
    {
        stream.Position = 0;

        uint size = 0xe0;   // sead::FrameHeap
        size += 0x20;       // sead::MemBlock
        size += 0x70;       // LayoutResource

        if (!SarcFileReader.CanRead(stream))
            return size;

        var sarcFile = SarcFileReader.Read(stream);
        var archiveShader = sarcFile.Files.FirstOrDefault(x => x.Name is "bgsh/__ArchiveShader.bnsh");

        if (archiveShader != null)
        {
            using var shaderData = new MemoryStream(archiveShader.Data);
            using var reader = new BinaryReader(shaderData);

            var offset = reader.ReadUInt16At(0x16);
            var variations = reader.ReadUInt32At(offset + 0x1c);
            Console.WriteLine($"Variation count is {variations} ({romfsName})");

            size += 0x20 * variations;
            size += 4 * variations;         // (vertex shader interface slots)
            size += 4 * variations;         // (geometry shader interface slots)
            size += 4 * variations;         // (fragment shader interface slots)
            size += 4 * 4 * variations;     // (texture slots)

            for (uint i = 0; i < variations; i++)
            {
                size += 8;          // nn::gfx::BufferInfo
                size += 4 * 0x10;   // NVNvertexAttribState
            }
        }
        else
        {
            Console.WriteLine($"{romfsName} don't have a archive shader.");
        }

        return size;
    }
}