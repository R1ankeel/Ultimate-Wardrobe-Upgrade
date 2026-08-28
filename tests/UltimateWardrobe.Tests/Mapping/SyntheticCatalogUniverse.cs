using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Runtime-synthesized catalog for the mapping layer (Phase 3 plan 3.0.5), the Mapping mirror of
/// the Phase 1 <c>SyntheticGroupingUniverse</c> / Phase 2 <c>SyntheticDonorUniverse</c> pattern.
/// A deterministically-shaped small catalog: one Iron set with Male Heavy and Female Heavy
/// variants, a couple of pieces each - enough for per-gender mapping, patch detection and status
/// derivation without any real files on disk.
/// </summary>
internal static class SyntheticCatalogUniverse
{
    public static Catalog CreateIronCatalog()
    {
        var male = new Variant(Gender.Male, WeightClass.Heavy, new[]
        {
            new Piece("ArmorIronCuirass", 0x00012E46, "32 Body", "AA_IronCuirass", "armor/iron/m/cuirass.nif"),
            new Piece("ArmorIronGauntlets", 0x00012E47, "36 Hands", "AA_IronGauntlets", "armor/iron/m/gauntlets.nif"),
        });

        var female = new Variant(Gender.Female, WeightClass.Heavy, new[]
        {
            new Piece("ArmorIronCuirassF", 0x00012ED0, "32 Body", "AA_IronCuirassF", "armor/iron/f/cuirass.nif"),
            new Piece("ArmorIronGauntletsF", 0x00012ED1, "36 Hands", "AA_IronGauntletsF", "armor/iron/f/gauntlets.nif"),
        });

        var set = new ArmorSet("IronArmor", "Iron Armor", new[] { male, female });
        return new Catalog(new VanillaCatalogSource("D:/Skymod/Stock Game"), new[] { set });
    }

    /// <summary>The male/female cuirass and gauntlets target pieces of the synthetic Iron set, flattened.</summary>
    public static IReadOnlyList<(string SetId, Piece Piece, Gender Gender)> TargetPieces(Catalog catalog)
    {
        var result = new List<(string, Piece, Gender)>();
        foreach (var set in catalog.Sets)
        {
            foreach (var variant in set.Variants)
            {
                foreach (var piece in variant.Pieces)
                {
                    result.Add((set.Id, piece, variant.Gender));
                }
            }
        }
        return result;
    }
}
