using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Branch-2 file discovery (Sprint 2.2.1): globs <c>meshes/**/*.nif</c> and
/// <c>textures/**/*.dds</c> under an extracted donor folder in both the root and the
/// <c>Data/</c> layout and returns game-relative paths - forward-slash normalized, WITH the
/// <c>meshes/</c>/<c>textures/</c> stem, and without any <c>Data/</c> prefix. Game-relative
/// paths keep <see cref="KeyNormalizer.NormalizeMeshFolder"/> keying and the Phase 5 slice
/// independent of the donor archive's folder layout, matching the branch-1
/// <see cref="FileResolver"/> convention.
/// </summary>
public sealed class MeshPathIndexer
{
    public IReadOnlyList<string> IndexMeshes(string extractedDir)
    {
        return Index(extractedDir, "meshes", ".nif");
    }

    public IReadOnlyList<string> IndexTextures(string extractedDir)
    {
        return Index(extractedDir, "textures", ".dds");
    }

    private static IReadOnlyList<string> Index(string extractedDir, string category, string extension)
    {
        if (string.IsNullOrWhiteSpace(extractedDir) || !Directory.Exists(extractedDir))
        {
            return Array.Empty<string>();
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        AddFromLayout(extractedDir, category, extension, found);
        var dataDir = Path.Combine(extractedDir, "Data");
        if (Directory.Exists(dataDir))
        {
            AddFromLayout(dataDir, category, extension, found);
        }

        return found.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static void AddFromLayout(string baseDir, string category, string extension, ISet<string> found)
    {
        var categoryDir = Path.Combine(baseDir, category);
        if (!Directory.Exists(categoryDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(categoryDir, "*" + extension, SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
            if (relative.StartsWith($"{category}/", StringComparison.Ordinal))
            {
                found.Add(relative);
            }
        }
    }
}