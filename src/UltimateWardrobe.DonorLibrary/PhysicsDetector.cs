namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Branch-3 physics detection (Sprint 2.3.2): finds HDT-SMP / CBPC / physics-patch artifacts.
/// Returns game-relative, ordinal, deduped paths. Detected:
/// <list type="bullet">
/// <item><c>SKSE/Plugins/hdtSMP64.dll</c>, <c>hdtSMP.xml</c>, <c>config.xml</c> and every
/// <c>*.json</c> under <c>SKSE/Plugins/</c> (CBPC configs) - recursive, both layouts.</item>
/// <item><c>*.tri</c> morphs - only when they sit under a folder that received a ProvidedSet
/// mesh (the caller passes the set mesh paths; an unrelated morph is not flagged).</item>
/// <item>any file whose name contains <c>hdt</c>/<c>smp</c>/<c>cbpc</c>/<c>physics</c>
/// (case-insensitive) anywhere in the folder.</item>
/// </list>
/// Deterministic by design. The caller is responsible for enumerating the folder itself via
/// <see cref="DonorTree"/> conventions; this detector only filters.
/// </summary>
public sealed class PhysicsDetector
{
    private static readonly string[] EngineFileNames =
    [
        "hdtSMP64.dll",
        "hdtSMP.xml",
        "config.xml",
    ];

    private static readonly string[] NameTokens =
    [
        "hdt",
        "smp",
        "cbpc",
        "physics",
    ];

    public IReadOnlyList<string> Detect(string extractedDir, IReadOnlyList<string>? setMeshPaths = null)
    {
        var setMeshDirs = (setMeshPaths ?? Array.Empty<string>())
            .Select(MeshDirectory)
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in DonorTree.EnumerateAll(extractedDir))
        {
            var name = Path.GetFileName(path);
            var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;

            if (NameTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(path);
                continue;
            }

            if (underSksePlugins(directory))
            {
                if (EngineFileNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                    name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(path);
                    continue;
                }
            }

            if (name.EndsWith(".tri", StringComparison.OrdinalIgnoreCase) &&
                setMeshDirs.Any(d => directory.Equals(d, StringComparison.Ordinal) ||
                                     directory.StartsWith(d + "/", StringComparison.Ordinal)))
            {
                found.Add(path);
            }
        }

        return found.OrderBy(p => p, StringComparer.Ordinal).ToList();

        static bool underSksePlugins(string directory)
        {
            return directory.Equals("SKSE/Plugins", StringComparison.Ordinal) ||
                   directory.StartsWith("SKSE/Plugins/", StringComparison.Ordinal);
        }
    }

    private static string MeshDirectory(string meshPath)
    {
        return Path.GetDirectoryName(meshPath)?.Replace('\\', '/') ?? string.Empty;
    }
}