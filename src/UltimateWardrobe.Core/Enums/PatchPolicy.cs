namespace UltimateWardrobe.Core.Enums;

/// <summary>
/// The machine-readable form of the roadmap 5.3 target-body / physics demand a user
/// chooses for an Overhaul ("user chose 3BA/HIMBO as the target body type"). Drives
/// <c>NeedsPatch</c> deterministically (Phase 3 <c>MappingService</c>) instead of relying
/// on a UI-only hint.
/// </summary>
public enum PatchPolicy
{
    /// <summary>
    /// Demand a patch only when the donor (or its attached patch) itself signals a
    /// conversion/physics need via its <c>Detected*</c> flags. The roadmap 5.3 default.
    /// </summary>
    Loose = 0,

    /// <summary>
    /// Additionally mark a mapping <c>NeedsPatch(Body)</c> when the donor set has a body
    /// piece but the donor lacks a BodySlide flag and no explicit body-type marker proves
    /// the mesh already targets the chosen body.
    /// </summary>
    RequireBodyConversion = 1,

    /// <summary>
    /// Additionally mark a mapping <c>NeedsPatch(Physics)</c> when no physics flags are
    /// present (donor or attached physics patch).
    /// </summary>
    RequirePhysics = 2,

    /// <summary>
    /// Both <see cref="RequireBodyConversion"/> and <see cref="RequirePhysics"/>.
    /// </summary>
    RequireBoth = 3
}
