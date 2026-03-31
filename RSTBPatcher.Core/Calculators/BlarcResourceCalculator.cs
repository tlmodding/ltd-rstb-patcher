using AeonSake.NintendoTools.FileFormats.Sarc;

namespace RSTBPatcher.Core.Calculators;

public class BlarcResourceCalculator : IResourceCalculator
{
    public static SarcFileReader SarcFileReader { get; } = new SarcFileReader();

    public static uint CalculateSizeOffset(Stream stream, string romfsName)
    {
        stream.Position = 0;

        if (!SarcFileReader.CanRead(stream))
            return 0;

        var sarcFile = SarcFileReader.Read(stream);
        uint size = 0;

        foreach (var item in sarcFile.Files)
        {
            var extension = Path.GetExtension(item.Name).ToLowerInvariant();

            if (extension is ".bntx" or ".bnsh")
                size += 0x1000;
        }

        return size;
    }
}