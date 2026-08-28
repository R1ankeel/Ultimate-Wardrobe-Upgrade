using Mutagen.Bethesda.Plugins;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Builds the ordered list of reference plugin files that load BEFORE the donor plugins when a
/// classification is enriched with reference game data (Sprint 2.1.1, Roadmap 2.1). Only
/// top-level <c>*.esm</c>/<c>*.esl</c> masters are enumerated (the <c>Data/</c> layout or a
/// root layout) - the same scope as the phase-1 vanilla discovery. Reference is purely
/// optional enrichment: a missing or empty reference root merges nothing. A reference file whose
/// name is owned by the donor set is excluded so the donor's bundled copy wins (donor
/// later-wins); within the reference itself, duplicate names keep the ordinal-first file. The
/// merged list is deterministic (ordinal by file name).
/// </summary>
public sealed class ReferenceMasterMerger
{
    private static readonly string[] ReferencePatterns = { "*.esm", "*.esl" };

    /// <summary>
    /// Merges the reference plugin list for a donor classification. Reference file names that
    /// are present in <paramref name="donorKeys"/> are dropped (dedupe against the donor set -
    /// the donor's bundled copy loads instead and wins). Returns absolute paths in load order.
    /// </summary>
    public IReadOnlyList<string> Merge(string? referenceRoot, IReadOnlySet<ModKey> donorKeys)
    {
        if (string.IsNullOrWhiteSpace(referenceRoot) || !Directory.Exists(referenceRoot))
        {
            return Array.Empty<string>();
        }

        var dataPath = ResolveDataPath(referenceRoot);
        if (!Directory.Exists(dataPath))
        {
            return Array.Empty<string>();
        }

        return ReferencePatterns
            .SelectMany(pattern => Directory.EnumerateFiles(dataPath, pattern, SearchOption.TopDirectoryOnly))
            .Select(file => (Path: file, Key: ModKey.FromFileName(Path.GetFileName(file))))
            .Where(f => f.Key.Name.Length > 0)
            .OrderBy(f => f.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .GroupBy(f => f.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(f => !donorKeys.Contains(f.Key))
            .Select(f => f.Path)
            .ToList();
    }

    private static string ResolveDataPath(string referenceRoot)
    {
        var dataPath = Path.Combine(referenceRoot, "Data");
        if (Directory.Exists(dataPath))
        {
            return dataPath;
        }

        if (Directory.EnumerateFiles(referenceRoot, "*.esm", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(referenceRoot, "*.esl", SearchOption.TopDirectoryOnly).Any())
        {
            return referenceRoot;
        }

        return dataPath;
    }
}