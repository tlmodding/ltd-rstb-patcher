using AeonSake.NintendoTools.Compression.Zstd;
using AeonSake.NintendoTools.FileFormats.Sarc;
using RSTBPatcher.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BinaryReader = AeonSake.BinaryTools.BinaryReader;

namespace RSTBPatcher.CLI;

public class Patcher
{
    internal static SarcFileReader SARCReader => Program.SARCReader;

    internal static ZstdDecompressor Decompressor => Program.Decompressor;

    private Dictionary<string, RESTBLFile.BaseEntry> pathMap = [];
    private Dictionary<uint, RESTBLFile.BaseEntry> hashMap = [];

    private ConcurrentBag<string> wrongFileTypes = [];
    private ConcurrentBag<string> correctFileTypes = [];

    private readonly ConcurrentBag<string> filesToCheck = [];
    private readonly ConcurrentDictionary<string, uint> filesToRemove = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, uint> filesToUpdate = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, uint> filesToAdd    = new(StringComparer.OrdinalIgnoreCase);

    public void CreatePatchParallel(Stream stream, string fileName, string outputPath, string moddedPath)
    {
        var stopwatch = Stopwatch.StartNew(); 
        var rstb = new RESTBLFile(stream);

        bool anyChange = false;

        pathMap = rstb.Entries
            .Where(e => !string.IsNullOrEmpty(e.Path))
            .GroupBy(e => e.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        hashMap = rstb.Entries
            .GroupBy(e => e.Hash)
            .ToDictionary(g => g.Key, g => g.First());

        var files = Directory.EnumerateFiles(moddedPath, "*", SearchOption.AllDirectories);

        var options = new ParallelOptions
        {
            //MaxDegreeOfParallelism = 1
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
        };

        Parallel.ForEach(files, options, originalFile =>
        {
            string path = Path.GetRelativePath(moddedPath, originalFile)
                .Replace(Path.DirectorySeparatorChar, '/');


            string ext = Path.GetExtension(path);

            if ((ext is ".zs" or ".mc") && !path.EndsWith(".ta.zs", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^3];
                ext = Path.GetExtension(path) ?? ext;
            }

            if (ext is ".rsizetable")
            {
                Console.WriteLine($"Skipping {path}...");
                return;
            }

            //using var decompressedStream = new MemoryStream();
            using var reader = File.OpenRead(originalFile);

           
            //    Decompressor.Decompress(reader, decompressedStream);
            //else reader.CopyTo(decompressedStream);

            //var decompressedSize = decompressedStream.Length;

            var blacklistedPacks = new List<string>()
            {
                "Pack/MiiPartsLocation.pack",
                "Pack/MiiExpression.pack",
                "Pack/MiiParts.pack"
            };

            long? overrideSize = null;
            if (ext is ".pack" && !blacklistedPacks.Contains(path))
            {
                Console.WriteLine($"> Opening pack... {path}");
            
                if (Decompressor.CanDecompress(reader))
                {
                    using var decompressedStream = new MemoryStream();
                    Decompressor.Decompress(reader, decompressedStream);
                    overrideSize = decompressedStream.Length;

                    decompressedStream.Position = 0;

                    if (SARCReader.CanRead(decompressedStream))
                    {
                        var sarc = SARCReader.Read(decompressedStream);
                        if (sarc.HasFileNames)
                            foreach (var sarcFile in sarc.Files)
                            {
                                var sarcExtension = Path.GetExtension(sarcFile.Name);
                                using var sarcStream = new MemoryStream(sarcFile.Data);
                                AddFile(sarcFile.Name, sarcStream);
                            }

                    }
                }
            }

            AddFile(path, reader, overrideSize);
        });

    

        wrongFileTypes = [.. wrongFileTypes.Distinct()];
        correctFileTypes = [.. correctFileTypes.Where(x => !wrongFileTypes.Contains(x)).Distinct()];

        Console.WriteLine("Matching Sizes:");


        Console.WriteLine(string.Join(", ", correctFileTypes));

        Console.WriteLine("--------------------------");

        Console.WriteLine("NOT Matching Sizes:");
        Console.WriteLine(string.Join(", ", wrongFileTypes));
        Console.WriteLine("--------------------------");
        Console.WriteLine($"{filesToAdd.Count} new files.");
        Console.WriteLine($"{filesToRemove.Count} removed files.");
        Console.WriteLine($"{filesToCheck.Count} files (RSTB have {rstb.Entries.Count} entries)");
        Console.WriteLine($"{filesToCheck.Distinct().Count()} files (RSTB have {rstb.Entries.Count} entries)");

        foreach (var kv in filesToRemove)
        {
            string path = kv.Key;
            uint pathHash = kv.Value;

            int removedCount = rstb.Entries.RemoveAll(x =>
                (x.Path is string p && p.Equals(path, StringComparison.OrdinalIgnoreCase)) ||
                x.Hash == pathHash);

            if (removedCount > 0)
                anyChange = true;
        }

        foreach (var kv in filesToUpdate)
        {
            string path = kv.Key;
            uint newSize = kv.Value;
            uint pathHash = path.ToCRC32();

            var entry = rstb.Entries.FirstOrDefault(x =>
                (x.Path is string p && p.Equals(path, StringComparison.OrdinalIgnoreCase)) ||
                x.Hash == pathHash);

            if (entry != null && entry.Size != newSize)
            {
                entry.Size = newSize;
                anyChange = true;
            }
        }

        if (anyChange)
        {
            var directory = Directory.CreateDirectory(outputPath);

            if (!directory.Exists)
                throw new Exception($"Failed to create directory: {outputPath}");

            var savePath = Path.Combine(outputPath, fileName);

            rstb.SaveTo(savePath);
            Console.WriteLine($"Patch created successfully at {savePath}");
        }
        else
        {
            Console.WriteLine("No changes detected, skipping patch creation.");
        }

        stopwatch.Stop();  

        // Output the elapsed time
        Console.WriteLine($"Patch creation completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
    }


    bool TryGetEntry(string path, uint hash, [MaybeNullWhen(false)] out RESTBLFile.BaseEntry entry)
    {
        if (pathMap.TryGetValue(path, out var pathEntry))
        {
            entry = pathEntry;
            return true;
        }

        if (hashMap.TryGetValue(hash, out var hashEntry))
        {
            entry = hashEntry;
            return true;
        }

        entry = null;
        return false;
    }

    bool AddFile(string path, Stream stream, long? overrideSize = null)
    {
        filesToCheck.Add(path);
        long fileSize = RESTBLFile.GetFileSize(stream, path, overrideSize);
        var pathHash = path.ToCRC32(); 

        if (fileSize < 0)
        {
            if (pathMap.ContainsKey(path) || hashMap.ContainsKey(pathHash))
            {
                filesToRemove.TryAdd(path, pathHash);
                Console.WriteLine($"Unsupported file, removing to avoid crashes... {path}");
            }

            return false;
        }

        if (fileSize > uint.MaxValue)
        {
            if (pathMap.ContainsKey(path) || hashMap.ContainsKey(pathHash))
            {
                filesToRemove.TryAdd(path, pathHash);
                Console.WriteLine($"{path} is way too big and will be removed...");
            }
            return false;
        }

        if (TryGetEntry(path, pathHash, out var entry))
        {
            var ext = Path.GetExtension(path);
            if (fileSize == entry.Size)
            {
                correctFileTypes.Add(ext);
                if (ext is ".blarc") Console.WriteLine($"{path} size matches!");
                return false;
            }

            wrongFileTypes.Add(ext);
            filesToUpdate[path] = (uint)fileSize;

            Console.WriteLine($"> {path} size mismatch! ({entry.Size} > {fileSize}) (Diff: {entry.Size - fileSize})");
        }
        else
        {
            Console.WriteLine($"{path} is new!");
            // TODO: Add new entries
            filesToAdd[path] = (uint)fileSize;
        }

        return true;
    }
}
