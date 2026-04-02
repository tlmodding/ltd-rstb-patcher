using AeonSake.NintendoTools.Compression.Zstd;
using AeonSake.NintendoTools.FileFormats.Sarc;
using RSTBPatcher.CLI;
using RSTBPatcher.Core;
using System.Collections.Concurrent;
using System.CommandLine;

internal class Program
{
    public static readonly string[] ValidationDirectories = [
        "AI", "AIBgmCtrlParam", "AnimationEvent", "AS", "ChimeSetting", "Component", "Data", "Editor", "Effect", "ELink2", "Env",
        "FocusLeading", "Font", "GameData", "House", "Icon", "Layout", "Lib", "Mals", "MapFile", "Mii", "MiiNewsParam", "MiiTouchCameraParam",
        "Model", "Pack", "Parameter", "Phive", "RSDB", "Rumble", "ScenarioChat", "Shader", "SLink2", "Sound", "SoundCameraLinkSetting",
        "SoundIgnoreDuckingSetting", "SoundLeakOutParam", "SoundLeakOutSetting", "SoundOutputDeviceSetting", "System", "Tex", "UI",
        "VoiceLanguageOffset", "VoicePlayParam", "VoiceText", "WalkingGrid", "Yukaidi"
    ];

    internal static readonly SarcFileReader SARCReader = new();

    internal static readonly ZstdDecompressor Decompressor = new();

    private static int Main(string[] args)
    {
        var inputOption = new Option<FileInfo>(name: "--input", "-i")
        {
            Required = true,
            Description = "Input file, must be a valid ResourceSizeTable."
        };

        inputOption.Validators.Add(result =>
        {
            var file = result.GetValue(inputOption);

            if (file is null || !file.Exists)
            {
                result.AddError("Input file does not exist.");
                return;
            }

            if (!IsValid(file))
            {
                result.AddError("Input file is not a valid .rsizetable");
                return;
            }
        });

        var moddedRomfsOption = new Option<string>(name: "--target", "-t")
        {
            Required = true,
            Description = "Folder containing the files that you want to patch",
        };

        moddedRomfsOption.Validators.Add(result =>
        {
            var dir = result.GetValue(moddedRomfsOption);

            if (dir == null || !Directory.Exists(dir))
            {
                result.AddError("Target directory doesn't exist, can't find your mods!");
                return;
            }

            var dirs = Directory.GetDirectories(dir).Select(d => Path.GetFileName(d) ?? "").ToArray();
            if (dirs.Length == 0)
            {
                result.AddError("Target directory is empty, there is nothing to patch.");
                return;
            }

            if (!ValidationDirectories.ContainsAny(dirs, StringComparer.OrdinalIgnoreCase))
                result.AddError("Target directory doesn't seem to be a valid modded RomFS, make sure to include all the files you want to patch into a single modded RomFS!");
        });


        var romfsOption = new Option<string>(name: "--romfs", "-r")
        {
            Required = true,
            Description = "The dumped VANILLA romfs",
        };

        romfsOption.Validators.Add(result =>
        {
            var dir = result.GetValue(romfsOption);

            if (dir == null || !Directory.Exists(dir))
            {
                result.AddError("Target directory doesn't exist, can't find your mods!");
                return;
            }

            var dirs = Directory.GetDirectories(dir).Select(d => Path.GetFileName(d) ?? "").ToArray();
            if (dirs.Length == 0)
            {
                result.AddError("Target directory is empty, there is nothing to patch.");
                return;
            }

            if (!ValidationDirectories.All(y => dirs.Contains(y)))
                result.AddError("Target directory doesn't seem to be a valid RomFS, it must contain ALL romfs folders!");
        });

        var outputOption = new Option<string>(name: "--output", "-o")
        {
            DefaultValueFactory = _ => "output",
            Description = "Output directory where the file will be saved.",
        };

        var createPatchCommand = new Command("patch", "Patches a ResourceSizeTable using your modded RomFS")
        {
            inputOption,
            outputOption,
            moddedRomfsOption
        };

        createPatchCommand.SetAction(h =>
        {
            var input = h.GetValue(inputOption);
            var output = h.GetValue(outputOption);
            var romfs = h.GetValue(moddedRomfsOption);

            if (input != null && output != null && romfs != null)
            {
                using var stream = input.OpenRead();
                var patcher = new Patcher();
                patcher.CreatePatchParallel(stream, input.Name, output, romfs);
                //CreatePatchParallel(stream, input.Name, output, romfs);
            }

            return 0;
        });

        var exportCommand = new Command("export", "Exports a ResourceSizeTable to CSV for analysis")
        {
            inputOption,
            romfsOption,
            outputOption
        };

        exportCommand.SetAction(h =>
        {
            var input = h.GetValue(inputOption);
            var romfs = h.GetValue(romfsOption);
            var output = h.GetValue(outputOption);


            if (input != null && romfs != null && output != null)
            {
                using var stream = input.OpenRead();

                ExportToCSV(input, romfs, output, stream);
            }
        });

        var rootCommand = new RootCommand("Tomodachi Life: Living the Dream - ResourceSizeTable Patcher")
        {
            createPatchCommand,
            exportCommand
        };

        return rootCommand.Parse(args).Invoke();
    }

    private static void ExportToCSV(FileInfo input, string romfs, string output, FileStream stream)
    {
        var rstb = new RESTBLFile(stream);
        Console.WriteLine($"Loaded RSTB with {rstb.Entries.Count} entries...");

        // Fast lookup for RSTB entries
        var pathMap = rstb.Entries
            .Where(e => !string.IsNullOrEmpty(e.Path))
            .GroupBy(e => e.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hashMap = rstb.Entries
            .GroupBy(e => e.Hash)
            .ToDictionary(g => g.Key, g => g.First());

        // Store discovered file info directly for fast export
        var foundByPath = new ConcurrentDictionary<string, (uint RstbSize, long ActualSize)>(StringComparer.OrdinalIgnoreCase);
        var foundByHash = new ConcurrentDictionary<uint, (string Path, uint RstbSize, long ActualSize)>();

        Console.WriteLine("Scanning files, this may take a while...");

        var files = Directory.EnumerateFiles(romfs, "*", SearchOption.AllDirectories);

        Parallel.ForEach(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            },
            file =>
            {
                var relativePath = Path.GetRelativePath(romfs, file)
                    .Replace(Path.DirectorySeparatorChar, '/');

                var normalizedPath = relativePath;
                var ext = Path.GetExtension(normalizedPath);

                if ((ext.Equals(".zs", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".mc", StringComparison.OrdinalIgnoreCase)) &&
                    !normalizedPath.EndsWith(".ta.zs", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = normalizedPath[..^3];
                    ext = Path.GetExtension(normalizedPath);
                }

                using var reader = File.OpenRead(file);
                using var decompressedStream = new MemoryStream();

                if (Decompressor.CanDecompress(reader))
                    Decompressor.Decompress(reader, decompressedStream);
                else
                    reader.CopyTo(decompressedStream);

                var decompressedSize = decompressedStream.Length;
                TryAddEntry(normalizedPath, decompressedSize);

                if (ext.Equals(".pack", StringComparison.OrdinalIgnoreCase))
                {
                    decompressedStream.Position = 0;

                    if (SARCReader.CanRead(decompressedStream))
                    {
                        var sarc = SARCReader.Read(decompressedStream);
                        if (sarc.HasFileNames)
                        {
                            foreach (var sarcFile in sarc.Files)
                            {
                                TryAddEntry(sarcFile.Name, sarcFile.Data.Length);
                            }
                        }
                    }
                }
            });

        Console.WriteLine("Exporting to CSV...");

        var csvLines = new List<string>(rstb.Entries.Count + 1)
    {
        "FileName,Extension,Full Extension,RSTB Entry Size,Size"
    };

        foreach (var entry in rstb.Entries)
        {
            var path = entry.Path ?? "";
            var rstbSize = entry.Size;
            long actualSize = 0;

            if (entry.Path != null)
            {
                if (foundByPath.TryGetValue(entry.Path, out var found))
                {
                    rstbSize = found.RstbSize;
                    actualSize = found.ActualSize;
                    path = entry.Path;
                }
            }
            else if (foundByHash.TryGetValue(entry.Hash, out var found))
            {
                path = found.Path;
                rstbSize = found.RstbSize;
                actualSize = found.ActualSize;
            }

            var extensionIndex = path.IndexOf('.');
            var fullExtension = extensionIndex >= 0 ? path[(extensionIndex + 1)..] : "";
            var extension = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToUpperInvariant();

            csvLines.Add($"{path},{extension},{fullExtension},{rstbSize},{actualSize}");
        }

        Directory.CreateDirectory(output);

        var outputPath = Path.Combine(output, $"{Path.GetFileNameWithoutExtension(input.Name)}.csv");
        File.WriteAllLines(outputPath, csvLines);

        Console.WriteLine($"Exported {csvLines.Count - 1}/{rstb.Entries.Count} rows to \"{outputPath}\" successfully!");

        void TryAddEntry(string filePath, long actualSize)
        {
            if (pathMap.TryGetValue(filePath, out var pathEntry))
            {
                foundByPath.TryAdd(filePath, (pathEntry.Size, actualSize));
                return;
            }

            var hash = filePath.ToCRC32();
            if (hashMap.TryGetValue(hash, out var hashEntry))
            {
                foundByHash.TryAdd(hash, (filePath, hashEntry.Size, actualSize));
            }
        }
    }

    public static bool IsValid(FileInfo file) => RESTBLFile.CanRead(file.OpenRead());
}