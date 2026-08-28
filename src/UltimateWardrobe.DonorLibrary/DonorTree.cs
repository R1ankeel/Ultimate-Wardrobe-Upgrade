namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Shared branch-3 file enumeration (Sprint 2.3): every file under the extracted donor folder in
/// BOTH the root and the <c>Data/</c> layout, normalized to game-relative forward-slash paths
/// (a leading <c>Data/</c> segment is dropped, matching <see cref="MeshPathIndexer"/>), deduped,
/// ordinal. The manifest keeps raw relative paths (with the <c>Data/</c> prefix where present) -
/// the Detected* lists deliberately use the game-relative convention so they line up with
/// branch-2 mesh paths and the Phase-1 <see cref="UltimateWardrobe.Scanner.FileResolver"/>
/// convention.
/// </summary>
internal static class DonorTree
{
    public static IReadOnlyList<string> EnumerateAll(string extractedDir)
    {
        if (string.IsNullOrWhiteSpace(extractedDir) || !Directory.Exists(extractedDir))
        {
            return Array.Empty<string>();
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(extractedDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(extractedDir, file).Replace('\\', '/');
            if (relative.StartsWith("Data/", StringComparison.Ordinal))
            {
                relative = relative["Data/".Length..];
            }

            found.Add(relative);
        }

        return found.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}