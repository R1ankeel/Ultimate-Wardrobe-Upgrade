using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Fixture helpers for the mapping tests (Phase 3 plan 3.0.5). <see cref="CreateOverhaulWithCatalog"/>
/// wires a project + Overhaul against a synthetic catalog; <see cref="CreateDonorOutput"/> builds a
/// post-classification <see cref="DonorAsset"/> (kind + flags + provided sets) for mapping onto targets.
/// </summary>
internal static class MappingFixtures
{
    /// <summary>
    /// A project + Overhaul over <paramref name="catalog"/>. The Overhaul's source is the catalog's
    /// source; its <see cref="Overhaul.Policy"/> defaults to <see cref="PatchPolicy.Loose"/>. Returns
    /// the project too so tests can add donors to <c>project.Library</c> for cross-project validation.
    /// </summary>
    public static (Project Project, Overhaul Overhaul) CreateOverhaulWithCatalog(
        Catalog catalog,
        PatchPolicy policy = PatchPolicy.Loose,
        string name = "VanillaOverhaul")
    {
        var project = new Project(Guid.NewGuid(), "TestProject", "C:/Projects/Test");
        var overhaul = new Overhaul(Guid.NewGuid(), name, project.Id, catalog.Source) { Policy = policy };
        return (project, overhaul);
    }

    /// <summary>
    /// A full replacer donor that provides a catalog-shaped Iron set whose pieces mirror the target
    /// ones (male/female cuirass + gauntlets), ready to be mapped onto an Iron target.
    /// </summary>
    public static DonorAsset CreateIronDonor(Guid projectId, string name = "donor-iron.7z")
    {
        return new DonorAsset(
            Guid.NewGuid(),
            name,
            $"C:/Project/Source/{Guid.NewGuid()}",
            DateTime.UtcNow,
            "abc-iron-hash",
            DonorAssetKind.FullReplacer,
            new[] { CreateDonorIronSet() });
    }

    /// <summary>
    /// A generic post-classification donor with explicit kind, flags and provided sets - the
    /// <c>CreateDonorOutput</c> helper: the shape the Phase 2 classifier emits for a donor.
    /// </summary>
    public static DonorAsset CreateDonorOutput(
        Guid projectId,
        DonorAssetKind kind = DonorAssetKind.FullReplacer,
        string name = "donor-output.7z",
        IReadOnlyList<string>? bodySlideFiles = null,
        IReadOnlyList<string>? physicsFiles = null,
        IReadOnlyList<DonorProvidedSet>? providedSets = null)
    {
        return new DonorAsset(
            Guid.NewGuid(),
            name,
            $"C:/Project/Source/{Guid.NewGuid()}",
            DateTime.UtcNow,
            "abc-output-hash",
            kind,
            providedSets,
            detectedBodySlideFiles: bodySlideFiles ?? Array.Empty<string>(),
            detectedPhysicsFiles: physicsFiles ?? Array.Empty<string>());
    }

    public static DonorProvidedSet CreateDonorIronSet(string id = "DonorIron", string displayName = "Donor Iron")
    {
        var male = new Variant(Gender.Male, WeightClass.Heavy, new[]
        {
            new Piece("DonorIronCuirass", 0x20012E46, "32 Body", "DA_IronCuirass", "armor/iron/m/cuirass.nif"),
            new Piece("DonorIronGauntlets", 0x20012E47, "36 Hands", "DA_IronGauntlets", "armor/iron/m/gauntlets.nif"),
        });

        var female = new Variant(Gender.Female, WeightClass.Heavy, new[]
        {
            new Piece("DonorIronCuirassF", 0x20012ED0, "32 Body", "DA_IronCuirassF", "armor/iron/f/cuirass.nif"),
            new Piece("DonorIronGauntletsF", 0x20012ED1, "36 Hands", "DA_IronGauntletsF", "armor/iron/f/gauntlets.nif"),
        });

        return new DonorProvidedSet(id, displayName, new[] { male, female });
    }
}
