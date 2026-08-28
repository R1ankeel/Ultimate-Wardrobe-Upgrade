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
/// The service is constructed for one project's <see cref="DonorLibrary"/>; every mutating method
/// (<see cref="AssignDonor"/>, <see cref="AttachPatch"/>, <see cref="Unassign"/>, <see cref="DetachPatch"/>)
/// takes the owning <see cref="Overhaul"/> so it can commit the immutable <see cref="PieceMapping"/>
/// into <see cref="Overhaul.Mappings"/> and enforce the cross-project invariant against
/// <see cref="DonorLibrary.Assets"/> (replaces / removes are applied only AFTER validation, so a
/// failing operation leaves no partial mapping). Sprint 3.1.
///
/// Status derivation (<see cref="GetStatus"/>) and the full <see cref="GetArmorSetStatus"/> table
/// land in Sprints 3.2 / 3.3; freshly assigned mappings are stamped <see cref="MappingStatus.Mapped"/>.
/// </summary>
public sealed class MappingService
{
    private readonly DonorLibrary _library;

    public MappingService(DonorLibrary donorLibrary)
    {
        _library = donorLibrary ?? throw new ArgumentNullException(nameof(donorLibrary));
    }

    /// <summary>
    /// Binds one target piece (per gender) to one donor piece. Resolves <see cref="PieceMapping.DonorMeshPath"/>
    /// from the donor piece, runs <see cref="PieceMapping.ValidateCrossProject"/> (donor must be in this
    /// project's library), and replaces any existing mapping with the same <see cref="PieceMapping.UniqueKey"/>.
    /// </summary>
    public PieceMapping AssignDonor(
        Overhaul overhaul,
        Catalog catalog,
        DonorAsset donorAsset,
        Piece targetPiece,
        Piece donorPiece)
    {
        if (overhaul is null) throw new ArgumentNullException(nameof(overhaul));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (donorAsset is null) throw new ArgumentNullException(nameof(donorAsset));
        if (targetPiece is null) throw new ArgumentNullException(nameof(targetPiece));
        if (donorPiece is null) throw new ArgumentNullException(nameof(donorPiece));

        var (setId, gender) = ResolveTargetContext(catalog, targetPiece);

        if (donorAsset.Kind is DonorAssetKind.BodyConversionPatch or DonorAssetKind.PhysicsPatch)
        {
            throw new InvalidOperationException(
                $"Donor asset {donorAsset.ImportId} is a patch ({donorAsset.Kind}) and cannot be used as the main donor.");
        }

        var meshPath = donorPiece.MeshPath ?? ""; // the PieceMapping ctor guards empty mesh path

        var newMapping = new PieceMapping(
            Guid.NewGuid(),
            overhaul.Id,
            setId,
            targetPiece.EditorId,
            gender,
            donorAsset.ImportId,
            donorPiece.EditorId,
            meshPath,
            status: MappingStatus.Mapped);

        newMapping.ValidateCrossProject(_library.Assets);

        overhaul.Mappings.RemoveAll(m => m.UniqueKey == newMapping.UniqueKey);
        overhaul.Mappings.Add(newMapping);

        return newMapping;
    }

    /// <summary>
    /// Attaches a body-conversion or physics patch layer to an assigned mapping. Enforces
    /// <paramref name="patchAsset"/>.<c>Kind</c> matches the requested layer and the patch belongs to
    /// this project's library (via <see cref="PieceMapping.ValidateCrossProject"/>); replaces the mapping
    /// in the Overhaul so no partial state remains on failure.
    /// </summary>
    public void AttachPatch(
        Overhaul overhaul,
        PieceMapping mapping,
        DonorAsset patchAsset,
        PatchKind patchKind)
    {
        if (overhaul is null) throw new ArgumentNullException(nameof(overhaul));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        if (patchAsset is null) throw new ArgumentNullException(nameof(patchAsset));

        var expectedKind = RequireExpectedPatchKind(patchKind);
        if (patchAsset.Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"Patch asset {patchAsset.ImportId} has Kind {patchAsset.Kind}, expected {expectedKind} for a {patchKind} layer.");
        }

        // Rebuild from the authoritative in-list mapping (by Id), so a stale caller-held instance
        // cannot clobber an already-attached layer.
        var current = RequireCurrentMapping(overhaul, mapping);
        var newMapping = patchKind switch
        {
            PatchKind.Body => Rebuild(current, patchAsset.ImportId, current.PhysicsPatchAssetId),
            PatchKind.Physics => Rebuild(current, current.BodyConversionPatchAssetId, patchAsset.ImportId),
            _ => throw new ArgumentOutOfRangeException(nameof(patchKind)),
        };

        newMapping.ValidateCrossProject(_library.Assets);

        ReplaceInList(overhaul, newMapping);
    }

    /// <summary>Removes an assigned mapping. Throws if the mapping is not part of the Overhaul.</summary>
    public void Unassign(Overhaul overhaul, PieceMapping mapping)
    {
        if (overhaul is null) throw new ArgumentNullException(nameof(overhaul));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));

        var removed = overhaul.Mappings.RemoveAll(m => m.Id == mapping.Id);
        if (removed == 0)
        {
            throw new InvalidOperationException($"Mapping {mapping.Id} is not part of Overhaul {overhaul.Id}.");
        }
    }

    /// <summary>Clears one patch layer (body or physics) of an assigned mapping.</summary>
    public void DetachPatch(
        Overhaul overhaul,
        PieceMapping mapping,
        PatchKind patchKind)
    {
        if (overhaul is null) throw new ArgumentNullException(nameof(overhaul));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));

        RequireExpectedPatchKind(patchKind);

        var current = RequireCurrentMapping(overhaul, mapping);
        var newMapping = patchKind switch
        {
            PatchKind.Body => Rebuild(current, null, current.PhysicsPatchAssetId),
            PatchKind.Physics => Rebuild(current, current.BodyConversionPatchAssetId, null),
            _ => throw new ArgumentOutOfRangeException(nameof(patchKind)),
        };

        newMapping.ValidateCrossProject(_library.Assets);

        ReplaceInList(overhaul, newMapping);
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

    private static (string SetId, Gender Gender) ResolveTargetContext(Catalog catalog, Piece targetPiece)
    {
        foreach (var set in catalog.Sets)
        {
            foreach (var variant in set.Variants)
            {
                if (variant.Pieces.Any(p => p.EditorId == targetPiece.EditorId))
                {
                    return (set.Id, variant.Gender);
                }
            }
        }

        throw new InvalidOperationException($"Target piece {targetPiece.EditorId} was not found in any catalog set.");
    }

    private static DonorAssetKind RequireExpectedPatchKind(PatchKind patchKind)
    {
        return patchKind switch
        {
            PatchKind.Body => DonorAssetKind.BodyConversionPatch,
            PatchKind.Physics => DonorAssetKind.PhysicsPatch,
            _ => throw new ArgumentOutOfRangeException(nameof(patchKind)),
        };
    }

    private static PieceMapping Rebuild(PieceMapping source, Guid? bodyPatch, Guid? physicsPatch)
    {
        return new PieceMapping(
            source.Id,
            source.OverhaulId,
            source.TargetArmorSetId,
            source.TargetPieceEditorId,
            source.TargetGender,
            source.DonorAssetId,
            source.DonorPieceEditorId,
            source.DonorMeshPath,
            bodyPatch,
            physicsPatch,
            source.Status,
            source.Notes);
    }

    private static PieceMapping RequireCurrentMapping(Overhaul overhaul, PieceMapping mapping)
    {
        var index = overhaul.Mappings.FindIndex(m => m.Id == mapping.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Mapping {mapping.Id} is not part of Overhaul {overhaul.Id}.");
        }

        return overhaul.Mappings[index];
    }

    private static void ReplaceInList(Overhaul overhaul, PieceMapping newMapping)
    {
        var index = overhaul.Mappings.FindIndex(m => m.Id == newMapping.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Mapping {newMapping.Id} is not part of Overhaul {overhaul.Id}.");
        }

        overhaul.Mappings[index] = newMapping;
    }
}
