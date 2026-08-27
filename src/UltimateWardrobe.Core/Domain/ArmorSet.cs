using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

public sealed class ArmorSet
{
    public string Id { get; init; }
    public string DisplayName { get; init; }
    public IReadOnlyList<Variant> Variants { get; init; }
    public ArmorSetStatus Status { get; init; }

    public ArmorSet(string id, string displayName, IReadOnlyList<Variant> variants, ArmorSetStatus status = ArmorSetStatus.NotStarted)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("DisplayName must not be empty.", nameof(displayName));
        if (variants is null) throw new ArgumentNullException(nameof(variants));

        Id = id;
        DisplayName = displayName;
        Variants = variants;
        Status = status;
    }
}
