using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

public sealed class Variant
{
    public Gender Gender { get; init; }
    public WeightClass Weight { get; init; }
    public IReadOnlyList<Piece> Pieces { get; init; }

    public Variant(Gender gender, WeightClass weight, IReadOnlyList<Piece> pieces)
    {
        if (pieces is null) throw new ArgumentNullException(nameof(pieces));
        Gender = gender;
        Weight = weight;
        Pieces = pieces;
    }
}
