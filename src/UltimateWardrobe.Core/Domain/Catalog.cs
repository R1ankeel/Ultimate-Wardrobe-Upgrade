using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

public sealed class ScanStats
{
    public int TotalArmo { get; init; }
    public int TotalArma { get; init; }
    public int GroupedSets { get; init; }
    public int Skipped { get; init; }
    public int MissingFiles { get; init; }

    /// <summary>
    /// Optional per-reason breakdown of <see cref="Skipped"/>. When present its values must
    /// sum to <see cref="Skipped"/>.
    /// </summary>
    public IReadOnlyDictionary<SkipReason, int> SkippedByReason { get; init; } = new Dictionary<SkipReason, int>();
}

public sealed class ScanWarning
{
    public string Message { get; init; }
    public string? EditorId { get; init; }

    public ScanWarning(string message, string? editorId = null)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message must not be empty.", nameof(message));
        Message = message;
        EditorId = editorId;
    }
}

public sealed class Catalog
{
    public CatalogSource Source { get; init; }
    public IReadOnlyList<ArmorSet> Sets { get; init; }
    public ScanStats Stats { get; init; }
    public IReadOnlyList<ScanWarning> Warnings { get; init; }

    public Catalog(CatalogSource source, IReadOnlyList<ArmorSet> sets, ScanStats? stats = null, IReadOnlyList<ScanWarning>? warnings = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Sets = sets ?? throw new ArgumentNullException(nameof(sets));
        Stats = stats ?? new ScanStats();
        Warnings = warnings ?? Array.Empty<ScanWarning>();
    }
}
