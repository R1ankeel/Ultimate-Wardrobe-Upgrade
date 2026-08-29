using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Plugin discovery inside an extracted donor folder (Sprint 2.0.5). Finds every
/// <c>*.esm</c>/<c>*.esl</c>/<c>*.esp</c> in both the root and the <c>Data/</c> subfolder layout,
/// then applies the frozen main-plugin rule: a candidate that no other candidate lists in its
/// <see cref="ModLoader.ReadMasters"/> set is a main candidate; prefer <c>esp</c> over
/// <c>esl</c> over <c>esm</c>, then ordinal by file name. A pure master chain (every candidate
/// referenced) reduces to the same extension/ordinal tie-break, which is deterministic.
/// Corrupt plugins are warned about and treated as master-less (last-choice candidates).
/// </summary>
public sealed record DonorPluginProbeResult
{
    public required string DataPath { get; init; }

    public required IReadOnlyList<DiscoveredPlugin> Candidates { get; init; }

    public DiscoveredPlugin? Main { get; init; }

    public required IReadOnlyList<ModKey> MainMasters { get; init; }
}

public sealed class DonorPluginProbe
{
    private static readonly string[] PluginPatterns = { "*.esm", "*.esl", "*.esp" };

    private readonly ModLoader _loader;
    private readonly ILogger<DonorPluginProbe> _logger;

    public DonorPluginProbe(ModLoader? loader = null, ILogger<DonorPluginProbe>? logger = null)
    {
        _loader = loader ?? new ModLoader();
        _logger = logger ?? NullLogger<DonorPluginProbe>.Instance;
    }

    public DonorPluginProbeResult Probe(string extractedDir, List<ScanWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(extractedDir)) throw new ArgumentException("Extracted donor folder must not be empty.", nameof(extractedDir));
        if (!Directory.Exists(extractedDir)) throw new DirectoryNotFoundException($"Donor folder '{extractedDir}' does not exist.");

        var dataDir = Path.Combine(extractedDir, "Data");
        var resolvedDataPath = Directory.Exists(dataDir) ? dataDir : extractedDir;

        // FOMOD/nested fix: donor archives often have plugins deep under subfolders like
        // "01 Core/data/[Christine] Dragon Marauder.esp" or "Main/Sub/Data/...". Search recursively
        // from the extraction root and deduplicate by absolute path.
        var candidates = Directory.EnumerateFiles(extractedDir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".esp", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(file => new DiscoveredPlugin { AbsolutePath = file, ModKey = ModKey.FromFileName(Path.GetFileName(file)) })
            .Where(p => p.ModKey.Name.Length > 0)
            .OrderBy(p => p.ModKey.Name, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogDebug("Donor plugin probe: no plugins found under '{Folder}'", extractedDir);
            return new DonorPluginProbeResult
            {
                DataPath = resolvedDataPath,
                Candidates = Array.Empty<DiscoveredPlugin>(),
                Main = null,
                MainMasters = Array.Empty<ModKey>(),
            };
        }

        var mastersByPath = new Dictionary<string, IReadOnlyList<ModKey>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            try
            {
                mastersByPath[candidate.AbsolutePath] = _loader.ReadMasters(candidate.AbsolutePath);
            }
            catch (Exception ex)
            {
                warnings.Add(new ScanWarning(
                    $"Donor plugin '{candidate.ModKey.Name}' could not be read and is treated as master-less: {ex.Message}"));
                mastersByPath[candidate.AbsolutePath] = Array.Empty<ModKey>();
            }
        }

        var referencedKeys = new HashSet<ModKey>();
        foreach (var candidate in candidates)
        {
            foreach (var master in mastersByPath[candidate.AbsolutePath])
            {
                if (candidates.Any(c => c.ModKey == master))
                {
                    referencedKeys.Add(master);
                }
            }
        }

        var main = candidates
            .OrderBy(p => referencedKeys.Contains(p.ModKey) ? 1 : 0)
            .ThenBy(p => ExtensionRank(p.ModKey))
            .ThenBy(p => p.ModKey.Name, StringComparer.Ordinal)
            .First();

        _logger.LogDebug(
            "Donor plugin probe: {CandidateCount} plugin(s) found in '{Folder}'; main plugin '{MainFileName}'",
            candidates.Count,
            extractedDir,
            main.ModKey.Name);

        return new DonorPluginProbeResult
        {
            DataPath = resolvedDataPath,
            Candidates = candidates,
            Main = main with { IsMainPlugin = true },
            MainMasters = mastersByPath[main.AbsolutePath],
        };
    }

    private static IEnumerable<string> EnumeratePlugins(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var pattern in PluginPatterns)
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }
    }

    private static int ExtensionRank(ModKey key)
    {
        return Path.GetExtension(key.FileName.ToString()).ToLowerInvariant() switch
        {
            ".esp" => 0,
            ".esl" => 1,
            ".esm" => 2,
            _ => 3,
        };
    }
}