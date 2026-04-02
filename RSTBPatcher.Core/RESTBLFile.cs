using AeonSake.NintendoTools.Compression.Zstd;
using RSTBPatcher.Core.Calculators;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using BinaryReader = AeonSake.BinaryTools.BinaryReader;
using BinaryWriter = AeonSake.BinaryTools.BinaryWriter;

namespace RSTBPatcher.Core;

public class RESTBLFile
{
    public static bool CanRead(Stream stream)
    {
        using var uncompressedStream = new MemoryStream();

        if (Decompressor.CanDecompress(stream))
            Decompressor.Decompress(stream, uncompressedStream);
        else stream.CopyTo(uncompressedStream);

        if (stream.CanSeek)
            stream.Seek(0, SeekOrigin.Begin);

        uncompressedStream.Position = 0;


        using var reader = new BinaryReader(uncompressedStream);

        return reader.ReadByteArray(6).SequenceEqual(MAGIC);
    }

    public record class BaseEntry(uint Hash, uint Size, string? Path)
    {
        public uint Size { get; set; } = Size;
    }

    public record class CRC32Entry(uint Hash, uint Size) : BaseEntry(Hash, Size, null);
    public record class PathEntry(string Path, uint Size) : BaseEntry(Path.ToCRC32(), Size, Path);

    public static readonly ZstdDecompressor Decompressor = new();
    public static readonly ZstdCompressor Compressor = new()
    {
        CompressionLevel = 17
    };

    public static byte[] MAGIC { get; } = "RESTBL"u8.ToArray();
    public int Version { get; set; }
    public int NameLength { get; set; }
    public bool WasCompressed { get; set; }

    public List<BaseEntry> Entries { get; set; }

    public bool TryGetEntry(string path, [MaybeNullWhen(false)] out BaseEntry entry)
    {
        entry = null;

        if (Entries == null || Entries.Count == 0)
            return false;

        var hash = path.ToCRC32();

        entry = Entries.FirstOrDefault(e => e.Hash == hash) ?? Entries.FirstOrDefault(e => e.Path == path);

        return entry != null;
    }

    public RESTBLFile(Stream stream)
    {
        var decompressedStream = new MemoryStream();
        if (Decompressor.CanDecompress(stream))
        {
            Decompressor.Decompress(stream, decompressedStream);
            WasCompressed = true;
        }
        else stream.CopyTo(decompressedStream);

        decompressedStream.Position = 0;

        using var reader = new BinaryReader(decompressedStream);

        if (!reader.ReadByteArray(6).SequenceEqual(MAGIC))
            throw new Exception("This is not a valid RESTBL file!");

        Version = reader.ReadInt32();
        NameLength = reader.ReadInt32();

        var crcEntries = reader.ReadInt32();
        var pathEntries = reader.ReadInt32();

        Entries = [];

        for (var i = 0; i < crcEntries; i++)
        {
            var hash = reader.ReadUInt32();
            var size = reader.ReadUInt32();

            Entries.Add(new CRC32Entry(hash, size));
        }

        for (var i = 0; i < pathEntries; i++)
        {
            var path = reader.ReadString(NameLength);
            var size = reader.ReadUInt32();

            Entries.Add(new PathEntry(path, size));
        }
    }

    public void Save(Stream stream)
    {
        using var uncompressedStream = new MemoryStream();
        using var writer = new BinaryWriter(uncompressedStream);

        writer.Write(MAGIC);
        writer.Write(Version);
        writer.Write(NameLength);

        var crc32Entries = Entries.Where(x => x is CRC32Entry).ToList();
        var pathEntries = Entries.Where(x => x is PathEntry).ToList();

        writer.Write(crc32Entries.Count);
        writer.Write(pathEntries.Count);

        foreach (var item in crc32Entries)
        {
            writer.Write(item.Hash);
            writer.Write(item.Size);
        }

        foreach (var item in pathEntries)
        {
            var bytes = Encoding.UTF8.GetBytes(item.Path ?? "");
            Array.Resize(ref bytes, NameLength);

            writer.Write(bytes);
            writer.Write(item.Size);
        }

        uncompressedStream.Position = 0;

        Compressor.Compress(uncompressedStream, stream);
    }

    public void SaveTo(string path)
    {
        using var stream = File.OpenWrite(path);
        Save(stream);
    }

    public static long GetFileSize(Stream stream, string romfsName, long? overrideSize = null)
    {
        var size = overrideSize ?? stream.Length;
        var extension = Path.GetExtension(romfsName);

        if (!overrideSize.HasValue && Decompressor.CanDecompress(stream))
            size = (long)ZstdDecompressor.GetDecompressedSize(stream);

        //var fileStream = new FileStream(entireFileName, FileMode.Open);

        //long size = 0;

        //using var decompressed = new MemoryStream();
        //if (entireFileName.EndsWith(".zs") || Decompressor.CanDecompress(fileStream))
        //{
        //    Decompressor.Decompress(fileStream, decompressed);
        //}
        //else 
        //    fileStream.CopyTo(decompressed);


        // Round up to the next number divisible by 32
        size = size + 31 & -32;


        return EstimateSize(size, stream, extension, romfsName);
    }

    public static long EstimateSize(long size, Stream stream, string extension, string romfsName)
    {
        return romfsName switch
        {
            "VoiceText/userdict_jpn.csv" => size + 0x138,
            "Font/Font.Nin_NX_NVN.bfarc" => size + 0xA20,
            "Sound/Resource/Mii_Static.bars" => size + 0x280,
            "Tex/Pack/MiiFaceMaskPos.bntx" => size + 0xE40,
            "Tex/Pack/MiiParts.bntx" => size + 0x960,
            _ => extension switch
            {
                ".ainb" => size + AinbResourceCalculator.CalculateSizeOffset(stream, romfsName),
                ".asb" => size + AsbResourceCalculator.CalculateSizeOffset(stream, romfsName),
                ".bgyml" => size + BgymlResourceCalculator.CalculateSizeOffset(stream, romfsName),
                ".baatarc" => size + 0x120,
                ".baev" => size + 0x140,
                ".bagst" => size + 0x120,
                ".bars" => size + 0x260,
                ".belnk" => size + 0x120,
                ".bfarc" => size + 0x120,
                ".bfsha" => size + 0x120,
                ".bhtmp" => size + 0x100,
                ".blarc" => size + BlarcResourceCalculator.CalculateSizeOffset(stream, romfsName),
                ".blwp" => size + 0x100,
                ".bnsh" => size + 0x9C8, // probably complex but there is only one file using this
                ".bntx" => size + 0x1000,
                ".bphcl" => size + 0x718,
                ".bphhb" => size + 0x100,
                ".bphnm" => size + 0x120,
                ".bphsh" => size + 0x190,
                ".bslnk" => size + 0x120,
                ".byml" => size + 0x120,
                ".genvb" => size + 0xE98, // probably complex
                ".pack" => size + 0x180,
                ".sarc" => size + 0x2000,
                ".txt" => size + 0x120,
                ".bin" => size + 0x120,
                ".vtdb2" => size + 0x120,
                ".csv" => size + 0x120,
                ".bwav" => -1,  // Should not be included in the RESTBL
                _ => (size + 1500) * 4
            }
        };
    }
}
