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
        if (path.StartsWith(SliderSets + "/", StringComparison.Ordinal) &&
            path.EndsWith(".osp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith(SliderGroups + "/", StringComparison.Ordinal) &&
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
        return string.Equals(directory, BodySlideRoot, StringComparison.Ordinal) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    }
}