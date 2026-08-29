namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Branch-3 BodySlide detection (Sprint 2.3.1): finds BodySlide artifacts under
/// <c>CalienteTools/BodySlide/</c> (root or <c>Data/</c> layout) and returns them as
/// game-relative, ordinal, deduped paths. Detected: any <c>*.osp</c> under
/// <c>SliderSets/</c> (recursive - real packs nest per-set folders), any <c>*.xml</c> under
/// <c>SliderGroups/</c> (recursive), and any <c>*.xml</c> directly under the BodySlide root
/// (the slider-group/preview xmls live at the top level; deeper non-group xml, e.g.
/// <c>DropdownData/</c>, is deliberately excluded). Deterministic by design.
/// </summary>
public sealed class BodySlideDetector
{
    private const string BodySlideRoot = "CalienteTools/BodySlide";
    private const string SliderSets = BodySlideRoot + "/SliderSets";
    private const string SliderGroups = BodySlideRoot + "/SliderGroups";

    public IReadOnlyList<string> Detect(string extractedDir)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in DonorTree.EnumerateAll(extractedDir))
        {
            if (IsBodySlidePath(path))
            {
                found.Add(path);
            }
        }

        return found.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static bool IsBodySlidePath(string path)
    {
        // FOMOD/nested fix: donor archives often have BodySlide deep under subfolders like
        // "01 Core/data/CalienteTools/BodySlide/..." or "[FB] Bishop Armor 3BA/CalienteTools/...".
        // Check for the segment anywhere in the game-relative path, not just at the start.
        if (path.IndexOf(SliderSets + "/", StringComparison.OrdinalIgnoreCase) >= 0 &&
            path.EndsWith(".osp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.IndexOf(SliderGroups + "/", StringComparison.OrdinalIgnoreCase) >= 0 &&
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Top-level BodySlide xml (e.g. CalienteTools/BodySlide/*.xml) - also handle nested prefix like "Prefix/CalienteTools/BodySlide/*.xml"
        var idx = path.IndexOf(BodySlideRoot + "/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var afterRoot = path[(idx + BodySlideRoot.Length + 1)..];
            // Direct child of BodySlide root ends with .xml and has no further slash (or is in SliderSets/SliderGroups which already handled)
            if (afterRoot.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !afterRoot.Contains('/', StringComparison.Ordinal))
            {
                return true;
            }

            // Also handle case where path is exactly "CalienteTools/BodySlide/*.xml" with prefix stripped to game-relative
            var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
            var dirIdx = directory.IndexOf(BodySlideRoot, StringComparison.OrdinalIgnoreCase);
            if (dirIdx >= 0)
            {
                var dirAfter = directory[dirIdx..];
                if (string.Equals(dirAfter, BodySlideRoot, StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}