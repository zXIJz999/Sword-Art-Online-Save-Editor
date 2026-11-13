using SaoSave.Dump;

// SaoSave.Dump --list <paks dir> <out.txt>                  every file in every pak (later paks win)
// SaoSave.Dump --extract <paks dir> <outDir> <filter>...    extract files whose path contains a filter
// SaoSave.Dump --show <uasset>                              every export of one asset with its fields
if (args.Length < 2) { Console.Error.WriteLine("usage: --list <paks dir> <out.txt> | --extract <paks dir> <outDir> <filter>... | --show <uasset>"); return 2; }

if (args[0] == "--show")
{
    var a = UAsset.Load(args[1]);
    for (int i = 0; i < a.Exports.Count; i++)
    {
        Console.WriteLine($"export {i + 1}: {a.Exports[i].Class} {a.Exports[i].Name}");
        foreach (var kv in a.Exports[i].Props) Console.WriteLine($"    {kv.Key} = {kv.Value.Replace(UAsset.StringTableMark, "ST:")}");
    }
    return 0;
}

if (args[0] == "--locres")
{
    var rows = Locres.Read(File.ReadAllBytes(args[1]));
    File.WriteAllLines(args[2], rows.Select(r => $"{r.ns}\t{r.key}\t{r.text.Replace('\t', ' ').Replace("\r", "").Replace('\n', ' ')}"));
    Console.WriteLine($"{rows.Count} texts");
    return 0;
}

// Paks are read in name order; a file in a later pak replaces the same path in an earlier one
// (the _p patch paks override the base ones).
var pakPaths = Directory.GetFiles(args[1], "*.pak").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
var paks = pakPaths.Select(p => new Pak(p, null)).ToList();
var files = new Dictionary<string, (Pak pak, Pak.Entry entry)>(StringComparer.OrdinalIgnoreCase);
var firstPak = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // which pak first holds each file
for (int i = 0; i < paks.Count; i++)
    foreach (var e in paks[i].Entries)
    {
        string path = (paks[i].MountPoint + e.Name).Replace("../../../", "");
        files[path] = (paks[i], e);
        firstPak.TryAdd(path, Path.GetFileNameWithoutExtension(pakPaths[i]));
    }
Console.WriteLine($"{paks.Count} paks, {files.Count} files");

switch (args[0])
{
    case "--list":
        File.WriteAllLines(args[2], files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}\t{f.Value.entry.UncompressedSize}\t{f.Value.entry.Compression}\t{firstPak[f.Key]}"));
        return 0;
    case "--extract":
    {
        var filters = args.Skip(3).ToArray();
        int n = 0, failed = 0;
        foreach (var (path, (pak, entry)) in files)
        {
            if (filters.Length > 0 && !filters.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var outPath = Path.Combine(args[2], path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, pak.Read(entry));
                n++;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
            {
                if (failed++ < 5) Console.WriteLine($"  skip {path}: {ex.Message}");
            }
        }
        Console.WriteLine($"extracted {n}, failed {failed}");
        return 0;
    }
    default:
        Console.Error.WriteLine("unknown mode");
        return 2;
}
