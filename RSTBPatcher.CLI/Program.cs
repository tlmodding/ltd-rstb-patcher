using AeonSake.NintendoTools.Compression.Zstd;
using AeonSake.NintendoTools.FileFormats.Sarc;
using RSTBPatcher.Core;
using System.Collections.Concurrent;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;

internal class Program
{
    public static readonly string[] ValidationDirectories = [ 
        "AI", "AIBgmCtrlParam", "AnimationEvent", "AS", "ChimeSetting", "Component", "Data", "Editor", "Effect", "ELink2", "Env",
        "FocusLeading", "Font", "GameData", "House", "Icon", "Layout", "Lib", "Mals", "MapFile", "Mii", "MiiNewsParam", "MiiTouchCameraParam",
        "Model", "Pack", "Parameter", "Phive", "RSDB", "Rumble", "ScenarioChat", "Shader", "SLink2", "Sound", "SoundCameraLinkSetting",
        "SoundIgnoreDuckingSetting", "SoundLeakOutParam", "SoundLeakOutSetting", "SoundOutputDeviceSetting", "System", "Tex", "UI",
        "VoiceLanguageOffset", "VoicePlayParam", "VoiceText", "WalkingGrid", "Yukaidi"
    ];

    private static readonly SarcFileReader SARCReader = new();

    private static readonly ZstdDecompressor Decompressor = new();

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
                CreatePatchParallel(stream, input.Name, output, romfs);
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
            FileInfo? input = h.GetValue(inputOption);
            string? romfs = h.GetValue(romfsOption);
            string? output = h.GetValue(outputOption);


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

        var dict = new ConcurrentDictionary<string, (uint RstbSize, long ActualSize)>(StringComparer.OrdinalIgnoreCase);

        var pathMap = rstb.Entries
            .Where(e => !string.IsNullOrEmpty(e.Path))
            .GroupBy(e => e.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hashMap = rstb.Entries
            .GroupBy(e => e.Hash)
            .ToDictionary(g => g.Key, g => g.First());

        Console.WriteLine("Scanning files, this may take a while...");

        var files = Directory.EnumerateFiles(romfs, "*", SearchOption.AllDirectories);

        Parallel.ForEach(files, file =>
        {
            var path = Path.GetRelativePath(romfs, file).Replace(Path.DirectorySeparatorChar, '/');

            string ext = Path.GetExtension(path);

            if (ext is ".zs" or ".mc" && !path.EndsWith(".ta.zs"))
            {
                path = path[..^3];
                ext = Path.GetExtension(path);
            }

            using var decompressedStream = new MemoryStream();
            using var reader = File.OpenRead(file);

            if (Decompressor.CanDecompress(reader))
                Decompressor.Decompress(reader, decompressedStream);
            else reader.CopyTo(decompressedStream);

            var decompressedSize = decompressedStream.Length;
            TryAddEntry(path, decompressedSize);

            if (ext is ".pack")
            {

                decompressedStream.Position = 0;

                if (SARCReader.CanRead(decompressedStream))
                {
                    var sarc = SARCReader.Read(decompressedStream);
                    if (sarc.HasFileNames)
                        foreach (var sarcFile in sarc.Files)
                            TryAddEntry(sarcFile.Name, sarcFile.Data.Length);

                }
            }

        });

        Console.WriteLine("Exporting to CSV...");

        var csvLines = new List<string> {
                    "FileName,Extension,Full Extension,RSTB Entry Size,Size"
                };

        foreach (var (path, (rstbEntry, size)) in dict)
        {
            int extensionIndex = path.IndexOf('.');
            string fullExtension = extensionIndex >= 0 ? path[(extensionIndex + 1)..] : "";
            string extension = Path.GetExtension(path).ToUpper();

            csvLines.Add($"{path},{extension},{fullExtension},{rstbEntry},{size}");

        }

        var outputPath = Path.Combine(output, $"{Path.GetFileNameWithoutExtension(input.Name)}.csv");
        File.WriteAllLines(outputPath, csvLines);
        Console.WriteLine($"Exported {csvLines.Count - 1} rows to \"{outputPath}\" successfully!");

        void TryAddEntry(string filePath, long actualSize)
        {
            if (pathMap.TryGetValue(filePath, out var pathEntry))
            {
                dict.TryAdd(filePath, (pathEntry.Size, actualSize));
                return;
            }

            uint hash = filePath.ToCRC32();
            if (hashMap.TryGetValue(hash, out var hashEntry))
            {
                dict.TryAdd(filePath, (hashEntry.Size, actualSize));
            }
        }
    }

    public static bool IsValid(FileInfo file) => RESTBLFile.CanRead(file.OpenRead());

    public static HashSet<string> WrongFileTypes = [];
    public static HashSet<string> CorrectFileTypes = [];

    public static void CreatePatch(Stream stream, string fileName, string outputPath, string moddedPath)
    {
        var rstb = new RESTBLFile(stream);

        var allFiles = Directory.GetFiles(moddedPath, "*", SearchOption.AllDirectories);
        bool anyChange = false;

        WrongFileTypes = [];
        CorrectFileTypes = [];

        for (int i = 0; i < allFiles.Length; i++)
        {
            string? originalFile = allFiles[i];

            var path = Path.GetRelativePath(moddedPath, originalFile).Replace(Path.DirectorySeparatorChar, '/');
            var pathHash = path.ToCRC32();

            string ext = Path.GetExtension(path);

            if (ext is ".zs" or ".mc" && !path.EndsWith(".ta.zs"))
            {
                path = path[..^3];
                ext = Path.GetExtension(path) ?? ext; // Update extension
            }

            // Skip Resource Size Table
            if (ext is ".rsizetable")
            {
                Console.WriteLine($"Skipping {path}...");
                continue;
            }

            var fileSize = RESTBLFile.GetFileSize(originalFile, path);

            if (fileSize < 0)
            {
                var removedCount = rstb.Entries.RemoveAll(x => (x.Path is string p && p.Equals(path, StringComparison.OrdinalIgnoreCase)) || x.Hash == pathHash);
                if (removedCount > 0)
                {
                    Console.WriteLine($"Unsupported file, removing to avoid crashes... {path}");
                    anyChange = true;
                }
                continue;
            }

            if (fileSize > uint.MaxValue)
            {
                var removedCount = rstb.Entries.RemoveAll(x => (x.Path is string p && p.Equals(path, StringComparison.OrdinalIgnoreCase)) || x.Hash == pathHash);
                if (removedCount > 0)
                {
                    Console.WriteLine($"{path} is way too big and will be removed...");
                    anyChange = true;
                }
                continue;
            }

            if (rstb.TryGetEntry(path, out var entry))
            {
                if (fileSize == entry.Size)
                {
                    CorrectFileTypes.Add(ext);
                    continue;
                }

                WrongFileTypes.Add(ext);
                Console.WriteLine($"{path} patched! ({entry.Size} > {fileSize}) (Diff: {entry.Size - fileSize})");
                entry.Size = (uint)fileSize;
                anyChange = true;
            }
            else
            {
                Console.WriteLine($"{path} is new!");
                //// Defaults new entry as 'path' entries
                //entry = new RESTBLFile.PathEntry(path, (uint)fileSize);
                //rstb.Entries.Add(entry);
                //anyChange = true;
            }
        }

        var types = CorrectFileTypes.Where(x => !WrongFileTypes.Contains(x));
        Console.WriteLine("The following file types were all correct:");
        foreach (var type in types) {
            Console.WriteLine(type);
        }

        if (anyChange)
        {
            var directory = Directory.CreateDirectory(outputPath);

            if (!directory.Exists)
                throw new Exception($"Failed to create directory: {outputPath}");

            var savePath = Path.Combine(outputPath, fileName);

            rstb.SaveTo(savePath);
            Console.WriteLine($"Patch created successfully at {savePath}");
        } else
        {
            Console.WriteLine("No changes detected, skipping patch creation.");
        }
    }

    public static void CreatePatchParallel(Stream stream, string fileName, string outputPath, string moddedPath)
    {
        var rstb = new RESTBLFile(stream);

        bool anyChange = false;

        WrongFileTypes = [];
        CorrectFileTypes = [];

        var wrongFileTypes = new ConcurrentBag<string>();
        var correctFileTypes = new ConcurrentBag<string>();

        var filesToRemove = new ConcurrentDictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var filesToUpdate = new ConcurrentDictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        var pathMap = rstb.Entries
            .Where(e => !string.IsNullOrEmpty(e.Path))
            .GroupBy(e => e.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hashMap = rstb.Entries
            .GroupBy(e => e.Hash)
            .ToDictionary(g => g.Key, g => g.First());

        var files = Directory.EnumerateFiles(moddedPath, "*", SearchOption.AllDirectories);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        };

        Parallel.ForEach(files, options, originalFile =>
        {
            string path = Path.GetRelativePath(moddedPath, originalFile)
                .Replace(Path.DirectorySeparatorChar, '/');

            uint pathHash = path.ToCRC32();
            string ext = Path.GetExtension(path);

            if ((ext is ".zs" or ".mc") && !path.EndsWith(".ta.zs", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^3];
                ext = Path.GetExtension(path) ?? ext;
                pathHash = path.ToCRC32();
            }

            if (ext is ".rsizetable")
            {
                Console.WriteLine($"Skipping {path}...");
                return;
            }

            long fileSize = RESTBLFile.GetFileSize(originalFile, path);

            if (fileSize < 0)
            {
                if (pathMap.ContainsKey(path) || hashMap.ContainsKey(pathHash))
                {
                    filesToRemove.TryAdd(path, pathHash);
                    Console.WriteLine($"Unsupported file, removing to avoid crashes... {path}");
                }
                return;
            }

            if (fileSize > uint.MaxValue)
            {
                if (pathMap.ContainsKey(path) || hashMap.ContainsKey(pathHash))
                {
                    filesToRemove.TryAdd(path, pathHash);
                    Console.WriteLine($"{path} is way too big and will be removed...");
                }
                return;
            }

            if (TryGetEntry(path, pathHash, out var entry))
            {
                if (fileSize == entry.Size)
                {
                    correctFileTypes.Add(ext);
                    return;
                }

                wrongFileTypes.Add(ext);
                filesToUpdate[path] = (uint)fileSize;

                Console.WriteLine($"{path} patched! ({entry.Size} > {fileSize}) (Diff: {entry.Size - fileSize})");
            }
            else
            {
                Console.WriteLine($"{path} is new!");
                // TODO: Add new entries
            }
        });

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

        wrongFileTypes = [.. wrongFileTypes.Distinct() ];
        correctFileTypes = [.. correctFileTypes.Where(x => !wrongFileTypes.Contains(x)).Distinct() ];

        Console.WriteLine("The following file types were all correct:");

        foreach (var type in correctFileTypes)
            Console.WriteLine(type);

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
    }
}