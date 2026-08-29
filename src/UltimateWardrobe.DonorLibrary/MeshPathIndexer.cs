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

        // FOMOD/nested fix: donor archives often have meshes deep under subfolders like
        // "01 Core/data/meshes/..." or "[FB] Bishop Armor 3BA/CalienteTools/.../meshes/...".
        // Use the shared DonorTree enumeration (recursive, Data/ prefix stripped at root) and
        // extract the game-relative path from the first occurrence of the category segment.
        foreach (var relative in DonorTree.EnumerateAll(extractedDir))
        {
            if (!relative.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idx = relative.IndexOf($"{category}/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var gameRelative = relative[idx..].Replace('\\', '/');
            if (gameRelative.StartsWith($"{category}/", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(gameRelative);
            }
        }

        return found.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}