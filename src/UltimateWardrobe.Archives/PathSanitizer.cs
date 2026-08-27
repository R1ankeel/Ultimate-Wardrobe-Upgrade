namespace UltimateWardrobe.Archives;

public static class PathSanitizer
{
    public static bool IsSafeEntry(string entryKey, out string sanitizedRelative)
    {
        sanitizedRelative = string.Empty;
        if (string.IsNullOrWhiteSpace(entryKey)) return false;

        // Normalize separators
        var normalized = entryKey.Replace('\\', '/').Trim();

        // Reject absolute paths, drive letters, UNC
        if (Path.IsPathRooted(normalized)) return false;
        if (normalized.Length >= 2 && normalized[1] == ':' && char.IsLetter(normalized[0])) return false;
        if (normalized.StartsWith("//", StringComparison.Ordinal) || normalized.StartsWith("\\\\", StringComparison.Ordinal)) return false;

        // Split and check for traversal
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == ".." || part == ".") return false;
            if (part.IndexOf(':') >= 0) return false;
            // Windows invalid chars in segment - we let file system handle, but reject obvious
        }

        if (parts.Length == 0) return false;

        sanitizedRelative = string.Join("/", parts);
        return true;
    }

    public static string GetSafeFullPath(string destDir, string sanitizedRelative)
    {
        // sanitizedRelative uses '/' as separator
        var parts = sanitizedRelative.Split('/');
        var combined = destDir;
        foreach (var p in parts) combined = Path.Combine(combined, p);
        var full = Path.GetFullPath(combined);
        var baseFull = Path.GetFullPath(destDir);
        if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            baseFull += Path.DirectorySeparatorChar;
        if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, baseFull.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path traversal detected: {sanitizedRelative}");
        }
        return full;
    }
}
