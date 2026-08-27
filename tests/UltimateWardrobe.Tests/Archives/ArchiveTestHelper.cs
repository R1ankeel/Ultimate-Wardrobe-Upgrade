using System.IO.Compression;

namespace UltimateWardrobe.Tests.Archives;

internal static class ArchiveTestHelper
{
    public static string CreateZipWithFiles(string dir, Dictionary<string, string> entries)
    {
        var zipPath = Path.Combine(dir, $"{Guid.NewGuid():N}.zip");
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var kv in entries)
        {
            var entry = zip.CreateEntry(kv.Key, CompressionLevel.Fastest);
            using var w = new StreamWriter(entry.Open());
            w.Write(kv.Value);
        }
        return zipPath;
    }

    public static string CreateNestedZip(string dir)
    {
        // inner zip
        var innerDir = Path.Combine(dir, "inner_src");
        Directory.CreateDirectory(innerDir);
        var innerPath = CreateZipWithFiles(innerDir, new Dictionary<string, string> { ["inner/file.txt"] = "inner-content" });

        // outer zip containing inner.zip + outer file
        var outerPath = Path.Combine(dir, $"{Guid.NewGuid():N}.zip");
        using var outerFs = File.Create(outerPath);
        using var outerZip = new ZipArchive(outerFs, ZipArchiveMode.Create);
        outerZip.CreateEntryFromFile(innerPath, "inner.zip");
        var e = outerZip.CreateEntry("outer.txt");
        using (var w = new StreamWriter(e.Open())) w.Write("outer-content");
        return outerPath;
    }

    public static string CreateTraversalZip(string dir)
    {
        var zipPath = Path.Combine(dir, $"{Guid.NewGuid():N}.zip");
        // Use ZipArchive low-level to craft traversal entry - ZipArchive will normalize but we can try
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        // This will be normalized by ZipArchive to remove .. but we can still test sanitizer via direct entry key
        // Instead craft manually via BinaryWriter? Simpler: create normal zip and test sanitizer unit
        // For extractor test, we create a zip via SharpCompress with traversal name if possible
        // Fallback: create normal
        var entry = zip.CreateEntry("../evil.txt");
        using (var w = new StreamWriter(entry.Open())) w.Write("evil");
        var good = zip.CreateEntry("good.txt");
        using (var gw = new StreamWriter(good.Open())) gw.Write("good");
        return zipPath;
    }

    public static void CreateTraversalViaSharpCompress(string zipPath)
    {
        // Create via System.IO.Compression - it preserves "../evil.txt" as entry name
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var evil = zip.CreateEntry("../evil.txt");
        using (var s = evil.Open())
        using (var w = new StreamWriter(s)) w.Write("evil");
        var good = zip.CreateEntry("good.txt");
        using (var gs = good.Open())
        using (var gw = new StreamWriter(gs)) gw.Write("good");
    }
}
