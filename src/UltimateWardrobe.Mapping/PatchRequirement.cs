namespace UltimateWardrobe.Mapping;

/// <summary>
/// The missing patch layer(s) for a mapping (Phase 3 plan 4.2). Independent body/physics bits so
/// <see cref="Both"/> equals <see cref="Body"/> | <see cref="Physics"/>. This is the deterministic
/// input to <see cref="MappingService.GetStatus"/> and <see cref="MappingService.RecommendPatches"/>.
/// </summary>
public enum PatchRequirement
{
    /// <summary>No patch layer is required - the mapping is <c>Mapped</c>.</summary>
    None = 0,

    /// <summary>A body-conversion patch is required.</summary>
    Body = 1,

    /// <summary>A physics patch is required.</summary>
    Physics = 2,

    /// <summary>Both a body-conversion and a physics patch are required.</summary>
    Both = Body | Physics
}
