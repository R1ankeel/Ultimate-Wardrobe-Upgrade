namespace UltimateWardrobe.Patcher;

/// <summary>
/// Sprint 5.2.1 - maps a game-relative path (e.g. <c>meshes/armor/iron/cuirass.nif</c>) to a
/// physical file inside an extracted donor folder. Supports both root and <c>Data/</c> layouts
/// (root first, then <c>Data</c>, mirroring the Phase 1 <see cref="Scanner.FileResolver"/> roots
/// and the branch-2 <c>MeshPathIndexer</c>/<c>DonorTree</c> consumption of the classifier).
/// A traversal guard rejects any path with a <c>..</c> or <c>.</c> segment and re-verifies that the
/// resolved full path stays under the extracted root, so a crafted manifest/mapping path can never
/// make the slicer read outside the donor folder. A missing file returns <see langword="null"/> -
/// the caller records the <see cref="Core.Abstractions.PatchWarning"/>, never an exception.
/// </summary>
public sealed class DonorFileLocator
{
    private readonly string _root;

    /// <summary>
    /// The normalized absolute extracted-donor root directory every resolved path must stay under.
    /// </summary>
    public string Root { get; }

    public DonorFileLocator(string extractedPath)
    {
        if (string.IsNullOrWhiteSpace(extractedPath)) throw new ArgumentException("ExtractedPath must not be empty.", nameof(extractedPath));
        _root = Path.GetFullPath(extractedPath);
        Root = _root;
    }

    /// <summary>
    /// Resolves <paramref name="gameRelativePath"/> to the physical full path of an existing file,
    /// trying <c>&lt;root&gt;/&lt;rel&gt;</c> then <c>&lt;root&gt;/Data/&lt;rel&gt;</c>. Returns
    /// <see langword="null"/> when the path is invalid (traversal, empty) or the file is absent.
    /// </summary>
    public string? TryLocate(string? gameRelativePath)
    {
        var relative = Sanitize(gameRelativePath);
        if (relative is null)
        {
            return null;
        }

        foreach (var candidateBase in new[] { _root, Path.Combine(_root, "Data") })
        {
            var full = Path.GetFullPath(Path.Combine(candidateBase, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithinRoot(full))
            {
                continue;
            }

            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    private bool IsWithinRoot(string candidate)
    {
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Slash/trim normalizes a lookup path and rejects traversal segments. Returns
    /// <see langword="null"/> for empty or unsafe input.
    /// </summary>
    private static string? Sanitize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment is ".." or "." or "")
            {
                return null;
            }
        }

        return normalized;
    }
}