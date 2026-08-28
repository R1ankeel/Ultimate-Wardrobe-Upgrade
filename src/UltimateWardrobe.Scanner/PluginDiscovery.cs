using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

public sealed record DiscoveredPlugin
{
    public required string AbsolutePath { get; init; }

    public required ModKey ModKey { get; init; }

    public bool IsMainPlugin { get; init; }

    /// <summary>
    /// True for plugins that are LINKED into the load order for master/FormLink resolution only -
    /// their ARMO/ARMA records are never scanned as catalog content (Sprint 6.9: the vanilla
    /// <c>Update.esm</c> resolution-only baseline). Explicitly requested plugins are never marked.
    /// </summary>
    public bool IsResolutionOnly { get; init; }
}

public sealed record DiscoveryResult
{
    public required string DataPath { get; init; }

    public required IReadOnlyList<DiscoveredPlugin> Plugins { get; init; }

    public required IReadOnlyList<ModKey> MissingExplicitMasters { get; init; }
}

public sealed class PluginDiscovery
{
    private static readonly string[] VanillaPatterns = { "*.esm", "*.esl" };

    /// <summary>
    /// The ONLY content plugins a Vanilla source scans when no explicit
    /// <see cref="VanillaCatalogSource.PluginNames"/> are requested (Sprint 6.9, T1):
    /// Skyrim.esm + the three official DLC masters. Any other esm/esl in the game Data
    /// folder (Creation Club content, _ResourcePack.esl, mod masters) is never scanned.
    /// </summary>
    private static readonly string[] VanillaOfficialMasters =
    {
        "Skyrim.esm",
        "Dawnguard.esm",
        "HearthFires.esm",
        "Dragonborn.esm",
    };

    /// <summary>
    /// The resolution-only vanilla baseline (Sprint 6.9, T2 of the fix): <c>Update.esm</c> is declared
    /// as a master by all three official DLC masters, so it is linked into the load order (no
    /// "missing master" warnings, DLC FormLinks stay resolvable) but is NEVER scanned for armor.
    /// </summary>
    private const string VanillaResolutionOnlyBaseline = "Update.esm";

    public DiscoveryResult Discover(CatalogSource source, List<ScanWarning> warnings)
    {
        if (source is StoryModCatalogSource story)
        {
            return DiscoverStory(source.RootPath, story.MainPlugin, story.Masters);
        }

        return DiscoverVanilla(source.RootPath, ((VanillaCatalogSource)source).PluginNames, warnings);
    }

    private static DiscoveryResult DiscoverVanilla(string rootPath, IReadOnlyList<string> pluginNames, List<ScanWarning> warnings)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new CatalogScanException($"Game folder '{rootPath}' does not exist. Point the source at the Skyrim game root (or an existing Data folder) and scan again.");
        }

        var dataPath = ResolveDataPath(rootPath);
        var plugins = new List<DiscoveredPlugin>();

        if (pluginNames.Count == 0)
        {
            var official = new HashSet<string>(VanillaOfficialMasters, StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in VanillaPatterns)
            {
                foreach (var file in EnumeratePluginFiles(dataPath, pattern))
                {
                    var modKey = ModKey.FromFileName(Path.GetFileName(file));
                    if (modKey.Name.Length == 0)
                    {
                        continue;
                    }

                    if (official.Contains(modKey.FileName.ToString()))
                    {
                        plugins.Add(new DiscoveredPlugin { AbsolutePath = file, ModKey = modKey });
                        continue;
                    }

                    if (modKey.FileName.ToString().Equals(VanillaResolutionOnlyBaseline, StringComparison.OrdinalIgnoreCase))
                    {
                        plugins.Add(new DiscoveredPlugin { AbsolutePath = file, ModKey = modKey, IsResolutionOnly = true });
                    }
                }
            }

            foreach (var master in VanillaOfficialMasters)
            {
                if (!plugins.Any(p => p.ModKey.FileName.ToString().Equals(master, StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add(new ScanWarning(
                        $"Official master '{master}' was not found under '{dataPath}'; it will not be scanned."));
                }
            }
        }
        else
        {
            foreach (var name in pluginNames)
            {
                var path = Path.Combine(dataPath, name);
                if (!File.Exists(path))
                {
                    warnings.Add(new ScanWarning($"Requested plugin '{name}' was not found under '{dataPath}'; skipping."));
                    continue;
                }

                plugins.Add(new DiscoveredPlugin { AbsolutePath = path, ModKey = ModKey.FromFileName(Path.GetFileName(path)) });
            }
        }

        return new DiscoveryResult
        {
            DataPath = dataPath,
            Plugins = plugins.OrderBy(p => p.ModKey.Name, StringComparer.Ordinal).ToList(),
            MissingExplicitMasters = Array.Empty<ModKey>(),
        };
    }

    private static DiscoveryResult DiscoverStory(string rootPath, string mainPlugin, IReadOnlyList<string> masters)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new CatalogScanException($"Mod folder '{rootPath}' does not exist. Verify the folder path and scan again.");
        }

        var mainPath = ResolvePath(rootPath, mainPlugin);
        if (!File.Exists(mainPath))
        {
            throw new CatalogScanException(
                $"Main plugin '{mainPlugin}' was not found under '{rootPath}'. Verify the mod folder and the main plugin file name, then scan again.");
        }

        var plugins = new List<DiscoveredPlugin>
        {
            new() { AbsolutePath = mainPath, ModKey = ModKey.FromFileName(Path.GetFileName(mainPath)), IsMainPlugin = true },
        };
        var missingMasters = new List<ModKey>();

        foreach (var master in masters)
        {
            var masterPath = ResolvePath(rootPath, master);
            if (!File.Exists(masterPath))
            {
                missingMasters.Add(ModKey.FromFileName(master));
                continue;
            }

            plugins.Add(new DiscoveredPlugin { AbsolutePath = masterPath, ModKey = ModKey.FromFileName(Path.GetFileName(masterPath)) });
        }

        return new DiscoveryResult
        {
            DataPath = ResolveDataPath(rootPath),
            Plugins = plugins,
            MissingExplicitMasters = missingMasters,
        };
    }

    private static IEnumerable<string> EnumeratePluginFiles(string dataPath, string pattern)
    {
        return Directory.Exists(dataPath)
            ? Directory.EnumerateFiles(dataPath, pattern, SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
    }

    private static string ResolveDataPath(string rootPath)
    {
        var dataPath = Path.Combine(rootPath, "Data");
        if (Directory.Exists(dataPath))
        {
            return dataPath;
        }

        if (EnumeratePluginFiles(rootPath, "*.esm").Any()
            || EnumeratePluginFiles(rootPath, "*.esl").Any()
            || EnumeratePluginFiles(rootPath, "*.esp").Any())
        {
            return rootPath;
        }

        return dataPath;
    }

    private static string ResolvePath(string rootPath, string pluginName)
    {
        return Path.IsPathRooted(pluginName) ? pluginName : Path.Combine(rootPath, pluginName);
    }
}