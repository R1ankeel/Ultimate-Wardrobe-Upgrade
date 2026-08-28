namespace UltimateWardrobe.Tests.DonorLibrary;

/// <summary>
/// The four donor archetypes the classifier distinguishes (Sprint 2.5.1). Deliberately FOUR,
/// not the roadmap's three: branch 3 emits <c>BodyConversionPatch</c> and <c>PhysicsPatch</c> as
/// separate kinds (plan 4.3), so each needs its own builder and golden snapshot.
/// </summary>
public enum SyntheticDonorArchetype
{
    EspFullReplacer,
    MeshOnlyReplacer,
    BodySlideOnlyPatch,
    PhysicsOnlyPatch
}

/// <summary>
/// Runtime builders for the four synthetic donor archetypes (Sprint 2.5.1), the DonorLibrary
/// mirror of the Phase 1 <c>SyntheticGroupingUniverse</c> pattern. Each archetype is written into
/// a fixed-Guid folder so the classifier yields a deterministic <c>ImportId</c>, making the
/// golden classification snapshots (2.5.3) byte-reproducible across runs and machines.
/// </summary>
internal static class SyntheticDonorUniverse
{
    public static IReadOnlyList<SyntheticDonorArchetype> All { get; } = new[]
    {
        SyntheticDonorArchetype.EspFullReplacer,
        SyntheticDonorArchetype.MeshOnlyReplacer,
        SyntheticDonorArchetype.BodySlideOnlyPatch,
        SyntheticDonorArchetype.PhysicsOnlyPatch,
    };

    /// <summary>
    /// Fixed folder names - each parses as a <see cref="Guid"/>, so the classifier's ImportId is
    /// deterministic (and unique per archetype).
    /// </summary>
    public static string FolderName(SyntheticDonorArchetype archetype) => archetype switch
    {
        SyntheticDonorArchetype.EspFullReplacer => "0f1e5f00-0000-0000-0000-000000000001",
        SyntheticDonorArchetype.MeshOnlyReplacer => "0f1e5f00-0000-0000-0000-000000000002",
        SyntheticDonorArchetype.BodySlideOnlyPatch => "0f1e5f00-0000-0000-0000-000000000003",
        SyntheticDonorArchetype.PhysicsOnlyPatch => "0f1e5f00-0000-0000-0000-000000000004",
        _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
    };

    /// <summary>
    /// Writes the archetype's runtime-synthesized files into a fresh <c>&lt;parent&gt;/&lt;guid&gt;</c>
    /// folder and returns that folder path. No Mutagen for the mesh/BodySlide/physics archetypes -
    /// plain files - only the esp archetype writes a plugin via <see cref="DonorModBuilder"/>.
    /// </summary>
    public static string Write(string parent, SyntheticDonorArchetype archetype)
    {
        var dir = Path.Combine(parent, FolderName(archetype));
        Directory.CreateDirectory(dir);

        switch (archetype)
        {
            case SyntheticDonorArchetype.EspFullReplacer:
                DonorModBuilder.WriteSelfContained(dir);
                break;
            case SyntheticDonorArchetype.MeshOnlyReplacer:
                DonorMeshTreeBuilder.Write(dir,
                    "meshes/armor/iron/f/cuirass.nif",
                    "meshes/armor/iron/f/gauntlets.nif",
                    "meshes/armor/iron/f/boots.nif",
                    "meshes/armor/iron/f/helmet.nif");
                break;
            case SyntheticDonorArchetype.BodySlideOnlyPatch:
                DonorMeshTreeBuilder.Write(dir, "CalienteTools/BodySlide/SliderSets/3BBB.osp");
                break;
            case SyntheticDonorArchetype.PhysicsOnlyPatch:
                DonorMeshTreeBuilder.Write(dir, "SKSE/Plugins/hdtSMP64.dll");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(archetype));
        }

        return dir;
    }
}
