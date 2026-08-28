using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Mapping;

/// <summary>
/// The manual-mapping API over the Phase 1 <see cref="Catalog"/>, the Phase 2 <see cref="DonorAsset"/>
/// set, and the <see cref="Overhaul"/>'s <see cref="Overhaul.Mappings"/> (Phase 3 plan 4.1).
///
/// Logic only, no I/O: the caller owns the collections (the <see cref="Overhaul"/>, the project's
/// donor <see cref="DonorLibrary"/>, and the <see cref="Catalog"/>) and passes them in. This project
/// depends on <see cref="UltimateWardrobe.Core.Domain"/> only.
///
/// Sprint 3.0 skeleton: the API surface exists; <see cref="GetArmorSetStatus"/> and
/// <see cref="GetOverhaulProgress"/> are implemented for the trivial (empty / unmapped) case so the
/// shape is testable. The CRUD (<see cref="AssignDonor"/>, <see cref="AttachPatch"/>,
/// <see cref="Unassign"/>, <see cref="DetachPatch"/>) and the full status derivation land in
/// Sprints 3.1 and 3.3.
/// </summary>
public sealed class MappingService
{
    /// <summary>
    /// Binds one target piece (per gender) to one donor piece. Replaces any existing mapping with
    /// the same <see cref="PieceMapping.UniqueKey"/>. Sprint 3.1.
    /// </summary>
    public PieceMapping AssignDonor(
        Overhaul overhaul,
        Catalog catalog,
        DonorAsset donorAsset,
        Piece targetPiece,
        Piece donorPiece)
    {
        throw new NotImplementedException("AssignDonor lands in Sprint 3.1.");
    }

    /// <summary>
    /// Attaches a body-conversion or physics patch layer to a mapping. Sprint 3.1.
    /// </summary>
    public void AttachPatch(
        PieceMapping mapping,
        DonorAsset patchAsset,
        PatchKind patchKind)
    {
        throw new NotImplementedException("AttachPatch lands in Sprint 3.1.");
    }

    /// <summary>Removes a mapping. Sprint 3.1.</summary>
    public void Unassign(PieceMapping mapping)
    {
        throw new NotImplementedException("Unassign lands in Sprint 3.1.");
    }

    /// <summary>Clears one patch layer (body or physics) of a mapping. Sprint 3.1.</summary>
    public void DetachPatch(PieceMapping mapping, PatchKind patchKind)
    {
        throw new NotImplementedException("DetachPatch lands in Sprint 3.1.");
    }

    /// <summary>
    /// Derives the <see cref="MappingStatus"/> for a single mapping from the donor's (or its
    /// attached patch layer's) Phase 2 flags and the Overhaul policy. Sprint 3.2.
    /// </summary>
    public MappingStatus GetStatus(
        PieceMapping mapping,
        DonorAsset donorAsset,
        DonorAsset? patchAssetBody = null,
        DonorAsset? patchAssetPhysics = null,
        PatchPolicy policy = PatchPolicy.Loose)
    {
        throw new NotImplementedException("GetStatus lands in Sprint 3.2.");
    }

    /// <summary>
    /// Per-set <see cref="ArmorSetStatus"/> (roadmap 5.4). Returns only the four stable values
    /// <see cref="ArmorSetStatus.NotStarted"/>/<see cref="ArmorSetStatus.InProgress"/>/
    /// <see cref="ArmorSetStatus.Mapped"/>/<see cref="ArmorSetStatus.NeedsPatch"/> - it never
    /// returns <see cref="ArmorSetStatus.Done"/> and takes no done-override (the overlay lives only
    /// in <see cref="GetOverhaulProgress"/>). A set with no mapping is <see cref="ArmorSetStatus.NotStarted"/>.
    /// </summary>
    public ArmorSetStatus GetArmorSetStatus(ArmorSet catalogSet, IReadOnlyList<PieceMapping> mappings)
    {
        if (catalogSet is null) throw new ArgumentNullException(nameof(catalogSet));
        if (mappings is null) throw new ArgumentNullException(nameof(mappings));

        var hasMapping = mappings.Any(m => m.TargetArmorSetId == catalogSet.Id);
        if (!hasMapping) return ArmorSetStatus.NotStarted;

        throw new NotImplementedException("Full ArmorSetStatus derivation lands in Sprint 3.3.");
    }

    /// <summary>
    /// Overhaul progress over the catalog sets. Counts a set as <see cref="OverhaulProgress.Done"/>
    /// only when its <see cref="GetArmorSetStatus"/> is <see cref="ArmorSetStatus.Mapped"/> and
    /// <paramref name="doneOverrides"/> marks it done. The sum invariant
    /// <c>Done + InProgress + NeedsPatch + NotStarted == TotalSets</c> always holds; the 200-set-style
    /// arithmetic is completed in Sprint 3.3.
    /// </summary>
    public OverhaulProgress GetOverhaulProgress(
        IReadOnlyList<PieceMapping> mappings,
        Catalog catalog,
        IReadOnlyDictionary<string, bool>? doneOverrides = null)
    {
        if (mappings is null) throw new ArgumentNullException(nameof(mappings));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        doneOverrides ??= new Dictionary<string, bool>();

        var notStarted = 0;
        var inProgress = 0;
        var mapped = 0;
        var needsPatch = 0;
        var done = 0;

        foreach (var set in catalog.Sets)
        {
            var status = GetArmorSetStatus(set, mappings);
            switch (status)
            {
                case ArmorSetStatus.NotStarted:
                    notStarted++;
                    break;
                case ArmorSetStatus.InProgress:
                    inProgress++;
                    break;
                case ArmorSetStatus.Mapped:
                    if (doneOverrides.TryGetValue(set.Id, out var isDone) && isDone)
                    {
                        done++;
                    }
                    else
                    {
                        mapped++;
                    }
                    break;
                case ArmorSetStatus.NeedsPatch:
                    needsPatch++;
                    break;
                default:
                    break;
            }
        }

        return new OverhaulProgress
        {
            TotalSets = catalog.Sets.Count,
            NotStarted = notStarted,
            InProgress = inProgress,
            Mapped = mapped,
            NeedsPatch = needsPatch,
            Done = done
        };
    }
}
