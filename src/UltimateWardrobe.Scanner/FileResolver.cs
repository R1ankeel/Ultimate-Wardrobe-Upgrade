using Microsoft.Extensions.Logging;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Resolves loose-file logical paths against a source folder, accounting for the
/// two root layouts: a standard game/mod layout (<c>RootPath/Data/...</c>) and a
/// folder-mod layout (<c>RootPath/...</c> with no <c>Data</c> wrapper). Missing
/// files only increment counters and log at Debug - never a warning flood.
/// </summary>
public sealed class FileResolver
{
    private readonly IReadOnlyList<string> _roots;
    private readonly ILogger? _logger;
    private int _missingFiles;

    public int MissingFiles => _missingFiles;

    public FileResolver(string rootPath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("RootPath must not be empty.", nameof(rootPath));
        _logger = logger;

        var dataPath = Path.Combine(rootPath, "Data");
        var roots = new List<string> { rootPath };
        if (Directory.Exists(dataPath))
        {
            roots.Add(dataPath);
        }

        _roots = roots;
    }

    /// <summary>
    /// Resolves a mesh logical path, e.g. <c>armor/iron/cuirass_1.nif</c>. Tries the
    /// <c>meshes/</c> stem under every candidate root, then the raw path. Returns the
    /// physical path with forward-slash normalization, or <see langword="null"/> and
    /// counts one missing file.
    /// </summary>
    public string? ResolveMesh(string logicalPath)
    {
        return Resolve(logicalPath, "meshes");
    }

    /// <summary>
    /// Resolves a texture logical path, e.g. <c>armor/iron/cuirass_1.dds</c>. Tries the
    /// <c>textures/</c> stem under every candidate root, then the raw path. Returns the
    /// physical path with forward-slash normalization, or <see langword="null"/> and
    /// counts one missing file.
    /// </summary>
    public string? ResolveTexture(string logicalPath)
    {
        return Resolve(logicalPath, "textures");
    }

    /// <summary>
    /// Resolves any logical path against the roots without injecting a category stem
    /// (mesh/texture). Used when the path already carries its own stem or is unrelated
    /// to meshes/textures. Returns the physical path, or <see langword="null"/>.
    /// </summary>
    public string? Resolve(string? logicalPath)
    {
        if (logicalPath is null)
        {
            return null;
        }

        return ResolveCandidates(AsCandidates(logicalPath, null));
    }

    private string? Resolve(string logicalPath, string category)
    {
        return ResolveCandidates(AsCandidates(logicalPath, category));
    }

    private IEnumerable<string> AsCandidates(string logicalPath, string? category)
    {
        if (string.IsNullOrWhiteSpace(logicalPath))
        {
            yield break;
        }

        var normalized = logicalPath.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0)
        {
            yield break;
        }

        foreach (var root in _roots)
        {
            if (string.IsNullOrEmpty(category))
            {
                yield return Combine(root, normalized);
                continue;
            }

            var withStem = normalized;
            if (!normalized.StartsWith(category + "/", StringComparison.OrdinalIgnoreCase))
            {
                withStem = $"{category}/{normalized}";
            }

            yield return Combine(root, withStem);
            yield return Combine(root, normalized);
        }
    }

    private static string Combine(string root, string relative)
    {
        var absolute = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return absolute;
    }

    private string? ResolveCandidates(IEnumerable<string> candidates)
    {
        string? first = null;
        foreach (var candidate in candidates)
        {
            first ??= candidate;
            if (File.Exists(candidate))
            {
                return candidate.Replace('\\', '/');
            }
        }

        if (first is not null)
        {
            _logger?.LogDebug("Missing loose file (first candidate): {Path}", first);
        }

        _missingFiles++;
        return null;
    }
}
