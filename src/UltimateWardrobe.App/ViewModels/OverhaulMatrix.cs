using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.App.ViewModels;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

/// <summary>
/// The role of one line in a mapped cell card (Phase 6 Sprint 6.4). Mirrors the wireframe's
/// "ARMOR 1 / LOAD ARMOR / LOAD 3ba(HIMBO) Patch / Load HDT-SMP Patch": a set line, one line per
/// distinct base donor, then one line per distinct attached BodyConversion/Physics patch.
/// </summary>
public enum CellLineRole
{
    Set,
    Donor,
    BodyPatch,
    PhysicsPatch,
}

/// <summary>
/// Base type of the flat, virtualizable matrix projection (Sprint 6.8, manual-testing bug 3): the
/// matrix body is one <see cref="IReadOnlyList{T}"/> of section headers and row bands served to a
/// single <c>VirtualizingStackPanel</c>-backed <c>ItemsControl</c>, so a 3000+ row catalog only
/// realizes the on-screen rows instead of the whole grid.
/// </summary>
public abstract class MatrixItemViewModel
{
}

/// <summary>One rendered line in a matrix cell card (Phase 6 Sprint 6.4).</summary>
public sealed record CellLineViewModel(string Text, CellLineRole Role);

/// <summary>
/// One weight column of the matrix (Phase 6 Sprint 6.4). Columns are the distinct weight classes
/// present in the catalog (Heavy / Light / Clothing / n/a), ordered deterministically.
/// </summary>
public sealed class MatrixColumnViewModel
{
    public WeightClass Weight { get; }
    public string Header { get; }

    public MatrixColumnViewModel(WeightClass weight)
    {
        Weight = weight;
        Header = OverhaulMatrix.Text(weight);
    }
}

/// <summary>
/// One cell of the matrix (Phase 6 Sprint 6.4): the (set, gender, weight) <see cref="Variant"/> for a
/// given section + weight column, its mapped card lines, and the set's <see cref="ArmorSetStatus"/>.
/// A missing variant (no variant for that set+section gender+weight) or a variant with no mapping is
/// blank - <see cref="IsBlank"/> true and <see cref="Lines"/> empty, so the view renders nothing.
/// </summary>
public sealed class MatrixCellViewModel
{
    public ArmorSet Set { get; }
    public Gender SectionGender { get; }
    public WeightClass Weight { get; }
    public Variant? Variant { get; }
    public IReadOnlyList<CellLineViewModel> Lines { get; }
    public ArmorSetStatus Status { get; }
    public bool IsBlank { get; }
    public bool IsStatusMatch { get; }

    public MatrixCellViewModel(
        ArmorSet set,
        Gender sectionGender,
        WeightClass weight,
        Variant? variant,
        IReadOnlyList<CellLineViewModel> lines,
        ArmorSetStatus status,
        bool isStatusMatch)
    {
        Set = set;
        SectionGender = sectionGender;
        Weight = weight;
        Variant = variant;
        Lines = lines;
        Status = status;
        IsStatusMatch = isStatusMatch;
        IsBlank = variant is null || lines.Count == 0;
    }

    public static MatrixCellViewModel Blank(
        ArmorSet set, Gender sectionGender, WeightClass weight, ArmorSetStatus status, bool isStatusMatch)
        => new(set, sectionGender, weight, null, Array.Empty<CellLineViewModel>(), status, isStatusMatch);
}

/// <summary>
/// One row of the matrix (Phase 6 Sprint 6.4): a catalog <see cref="Set"/> within a gender section,
/// with one cell per weight column. Rows are <see cref="IReadOnlyList{T}"/> projections (never an
/// observable-collection matrix rebuild - 2-D virtualization constraint 4.5). <see cref="DefaultCell"/>
/// feeds the popover editor when the row's name is clicked (Sprint 6.8, manual-testing bug 6).
/// </summary>
public sealed class ArmorSetRowViewModel : MatrixItemViewModel
{
    public ArmorSet Set { get; }
    public Gender SectionGender { get; }
    public string DisplayName => Set.DisplayName;
    public ArmorSetStatus Status { get; }
    public bool IsStatusMatch { get; }
    public IReadOnlyList<MatrixCellViewModel> Cells { get; }
    public MatrixCellViewModel DefaultCell { get; }

    public ArmorSetRowViewModel(
        ArmorSet set,
        Gender sectionGender,
        ArmorSetStatus status,
        bool isStatusMatch,
        IReadOnlyList<MatrixCellViewModel> cells,
        MatrixCellViewModel defaultCell)
    {
        Set = set;
        SectionGender = sectionGender;
        Status = status;
        IsStatusMatch = isStatusMatch;
        Cells = cells;
        DefaultCell = defaultCell;
    }
}

/// <summary>
/// One gender heading of the flat matrix projection (Sprint 6.8): a lightweight item that renders
/// "FEMALE ARMOR" / "MALE ARMOR" between the row bands it precedes.
/// </summary>
public sealed class MatrixSectionHeaderViewModel : MatrixItemViewModel
{
    public Gender Gender { get; }
    public string Header { get; }

    public MatrixSectionHeaderViewModel(Gender gender)
    {
        Gender = gender;
        Header = OverhaulMatrix.Text(gender);
    }
}

/// <summary>
/// One gender section of the matrix (Phase 6 Sprint 6.4): FEMALE ARMOR before MALE ARMOR. A catalog
/// set appears in the female section when it has a Female/Unisex variant and in the male section when
/// it has a Male/Unisex variant. Rows are the search-filtered projection.
/// </summary>
public sealed class MatrixSectionViewModel
{
    public Gender Gender { get; }
    public string Header { get; }
    public IReadOnlyList<ArmorSetRowViewModel> Rows { get; }

    public MatrixSectionViewModel(Gender gender, IReadOnlyList<ArmorSetRowViewModel> rows)
    {
        Gender = gender;
        Header = OverhaulMatrix.Text(gender);
        Rows = rows;
    }
}

/// <summary>
/// The immutable result of projecting one catalog into the FEMALE/MALE matrix (Phase 6 Sprint 6.4):
/// the ordered weight columns and the ordered gender sections with their row-band projections.
/// <see cref="MatrixItems"/> is the flat, virtualizable projection of the same data (Sprint 6.8):
/// section headers and row bands in order, served to one <c>VirtualizingStackPanel</c>.
/// </summary>
public sealed record OverhaulMatrixViewModel(
    IReadOnlyList<MatrixColumnViewModel> Columns,
    IReadOnlyList<MatrixSectionViewModel> Sections,
    IReadOnlyList<MatrixItemViewModel> MatrixItems);

/// <summary>
/// Pure projection of a catalog into the mapping matrix (Phase 6 Sprint 6.4, amendment 8). Column
/// order (Heavy, Light, Clothing, n/a), FEMALE-before-MALE section order, cell identity per
/// (set, gender, weight), set status via <see cref="MappingService.GetArmorSetStatus"/> and search
/// row-band filtering live here so they are directly unit-testable. A Unisex variant appears in both
/// gender sections.
/// </summary>
internal static class OverhaulMatrix
{
    /// <summary>Sections are emitted in this gender order when present.</summary>
    private static readonly (Gender Gender, bool Include)[] SectionOrder =
    {
        (Gender.Female, false),
        (Gender.Male, false),
    };

    // C2 - catalog-level cache: columns + per-set metadata, reused across filter invocations.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Catalog, CachedCatalogData> _catalogCache = new();

    private sealed class CachedCatalogData
    {
        public required IReadOnlyList<MatrixColumnViewModel> Columns { get; init; }
        public required IReadOnlyDictionary<string, SetMeta> SetMetaById { get; init; }
    }

    private sealed class SetMeta
    {
        public required ArmorSet Set { get; init; }
        public required string DisplayNameLower { get; init; }
        public required bool BelongsFemale { get; init; }
        public required bool BelongsMale { get; init; }
        public required Dictionary<(Gender SectionGender, WeightClass Weight), Variant?> VariantBySectionWeight { get; init; }

        public bool BelongsToSection(Gender sectionGender)
            => sectionGender == Gender.Female ? BelongsFemale : BelongsMale;

        public Variant? GetVariant(Gender sectionGender, WeightClass weight)
            => VariantBySectionWeight.TryGetValue((sectionGender, weight), out var v) ? v : null;
    }

    private static CachedCatalogData GetOrCreateCachedCatalogData(Catalog catalog)
    {
        if (_catalogCache.TryGetValue(catalog, out var cached))
        {
            return cached;
        }

        var data = BuildCachedCatalogData(catalog);
        _catalogCache.Add(catalog, data);
        return data;
    }

    private static CachedCatalogData BuildCachedCatalogData(Catalog catalog)
    {
        var columns = BuildColumns(catalog);
        var setMetaById = new Dictionary<string, SetMeta>(catalog.Sets.Count);
        foreach (var set in catalog.Sets)
        {
            var displayNameLower = set.DisplayName.ToLowerInvariant();
            var belongsFemale = set.Variants.Any(v => v.Gender == Gender.Female || v.Gender == Gender.Unisex);
            var belongsMale = set.Variants.Any(v => v.Gender == Gender.Male || v.Gender == Gender.Unisex);
            var variantBySectionWeight = new Dictionary<(Gender, WeightClass), Variant?>(capacity: SectionOrder.Length * columns.Count);
            foreach (var (sectionGender, _) in SectionOrder)
            {
                foreach (var col in columns)
                {
                    var variant = set.Variants.FirstOrDefault(v =>
                        v.Weight == col.Weight && (v.Gender == sectionGender || v.Gender == Gender.Unisex));
                    variantBySectionWeight[(sectionGender, col.Weight)] = variant;
                }
            }

            setMetaById[set.Id] = new SetMeta
            {
                Set = set,
                DisplayNameLower = displayNameLower,
                BelongsFemale = belongsFemale,
                BelongsMale = belongsMale,
                VariantBySectionWeight = variantBySectionWeight,
            };
        }

        return new CachedCatalogData
        {
            Columns = columns,
            SetMetaById = setMetaById,
        };
    }

    public static OverhaulMatrixViewModel Build(
        Catalog catalog,
        IReadOnlyList<PieceMapping> mappings,
        DonorLibraryModel library,
        MappingService mapping,
        string? search,
        ArmorSetStatus? statusFilter = null)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (mappings is null) throw new ArgumentNullException(nameof(mappings));
        if (library is null) throw new ArgumentNullException(nameof(library));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));

        // C2 - reuse columns and set metadata per catalog
        var cached = GetOrCreateCachedCatalogData(catalog);
        var columns = cached.Columns;
        var setMetaById = cached.SetMetaById;

        // C5 - normalize search once, use lowerInvariant + Ordinal
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var searchLower = normalizedSearch?.ToLowerInvariant();

        // C1 - index donor library: O(D) once
        var donorById = new Dictionary<Guid, DonorAsset>(library.Assets.Count);
        foreach (var asset in library.Assets)
        {
            donorById[asset.ImportId] = asset;
        }

        // C3 - index mappings: O(M) once
        var mappingsByKey = new Dictionary<(string SetId, string PieceId, Gender Gender), PieceMapping>(mappings.Count);
        var mappingsBySet = new Dictionary<string, List<PieceMapping>>(catalog.Sets.Count);
        foreach (var m in mappings)
        {
            mappingsByKey[(m.TargetArmorSetId, m.TargetPieceEditorId, m.TargetGender)] = m;
            if (!mappingsBySet.TryGetValue(m.TargetArmorSetId, out var list))
            {
                list = new List<PieceMapping>();
                mappingsBySet[m.TargetArmorSetId] = list;
            }

            list.Add(m);
        }

        // D2 - text-search prefilter before heavy cell building: search -> BelongsToSection -> status -> cells,
        // so non-matching sets do not pay status + cell cost. Status is computed lazily per passing set
        // and cached per Build to avoid double compute for Unisex sets appearing in both sections.
        // D1 - evaluated: ICollectionView over MatrixItems would still iterate S and would require
        // keeping MatrixItems stable across filters, complicating invalidation when mappings change.
        // Current Build with search prefilter already avoids rebuilding cells for filtered-out sets and
        // per-filter cost is O(D + M + S_passing * P) with hash lookups, so Build is kept.
        var statusCache = new Dictionary<string, ArmorSetStatus>(catalog.Sets.Count);
        var sections = new List<MatrixSectionViewModel>();
        var items = new List<MatrixItemViewModel>();
        foreach (var (sectionGender, _) in SectionOrder)
        {
            var rows = new List<ArmorSetRowViewModel>();
            foreach (var set in catalog.Sets)
            {
                var meta = setMetaById[set.Id];
                if (!meta.BelongsToSection(sectionGender))
                {
                    continue;
                }

                // C5 - search predicate on pre-lowercased name - first filter, O(1) per set
                if (searchLower is not null
                    && !meta.DisplayNameLower.Contains(searchLower, StringComparison.Ordinal))
                {
                    continue;
                }

                // C4 - status per passing set only, cached per Build for Unisex duplicate rows
                if (!statusCache.TryGetValue(set.Id, out var status))
                {
                    status = ComputeStatusFast(set, mappingsBySet, mappingsByKey);
                    statusCache[set.Id] = status;
                }
                var isMatch = statusFilter.HasValue && status == statusFilter.Value;
                var cells = new List<MatrixCellViewModel>(columns.Count);
                foreach (var col in columns)
                {
                    var variant = meta.GetVariant(sectionGender, col.Weight);
                    cells.Add(BuildCellFast(set, sectionGender, col.Weight, variant, mappingsByKey, mappingsBySet, donorById, status, isMatch));
                }

                var defaultCell = DefaultCellForFast(meta, sectionGender, columns, cells);
                rows.Add(new ArmorSetRowViewModel(set, sectionGender, status, isMatch, cells, defaultCell));
            }

            if (rows.Count > 0)
            {
                sections.Add(new MatrixSectionViewModel(sectionGender, rows));
                items.Add(new MatrixSectionHeaderViewModel(sectionGender));
                items.AddRange(rows);
            }
        }

        return new OverhaulMatrixViewModel(columns, sections, items);
    }

    private static ArmorSetStatus ComputeStatusFast(
        ArmorSet set,
        Dictionary<string, List<PieceMapping>> mappingsBySet,
        Dictionary<(string SetId, string PieceId, Gender Gender), PieceMapping> mappingsByKey)
    {
        var totalPieces = 0;
        var mappedPieces = 0;
        var anyNeedsPatch = false;

        foreach (var variant in set.Variants)
        {
            foreach (var piece in variant.Pieces)
            {
                totalPieces++;
                if (mappingsByKey.TryGetValue((set.Id, piece.EditorId, variant.Gender), out var m))
                {
                    mappedPieces++;
                    if (m.Status == MappingStatus.NeedsPatch)
                    {
                        anyNeedsPatch = true;
                    }
                }
            }
        }

        if (mappedPieces == 0) return ArmorSetStatus.NotStarted;
        if (anyNeedsPatch) return ArmorSetStatus.NeedsPatch;
        if (mappedPieces == totalPieces) return ArmorSetStatus.Mapped;
        return ArmorSetStatus.InProgress;
    }

    private static bool TryGetMappingForPiece(
        string setId,
        string pieceEditorId,
        Gender variantGender,
        Dictionary<(string SetId, string PieceId, Gender Gender), PieceMapping> mappingsByKey,
        Dictionary<string, List<PieceMapping>> mappingsBySet,
        out PieceMapping mapping)
    {
        if (mappingsByKey.TryGetValue((setId, pieceEditorId, variantGender), out mapping!))
        {
            return true;
        }

        // Unisex variant matches any gender mapping for that piece (original PiecesMappingsFor semantics)
        if (variantGender == Gender.Unisex)
        {
            if (mappingsBySet.TryGetValue(setId, out var list))
            {
                foreach (var candidate in list)
                {
                    if (candidate.TargetPieceEditorId == pieceEditorId)
                    {
                        mapping = candidate;
                        return true;
                    }
                }
            }
        }

        mapping = null!;
        return false;
    }

    private static List<PieceMapping> GetMappingsForVariantFast(
        ArmorSet set,
        Variant variant,
        Dictionary<(string SetId, string PieceId, Gender Gender), PieceMapping> mappingsByKey,
        Dictionary<string, List<PieceMapping>> mappingsBySet)
    {
        var result = new List<PieceMapping>(variant.Pieces.Count);
        foreach (var piece in variant.Pieces)
        {
            if (TryGetMappingForPiece(set.Id, piece.EditorId, variant.Gender, mappingsByKey, mappingsBySet, out var m))
            {
                result.Add(m);
            }
        }

        return result;
    }

    /// <summary>
    /// Picks the editable row coordinate for the row-name click (Sprint 6.8, manual-testing bug 6):
    /// the first weight column that carries a variant for the section gender, so the popover can open
    /// even when nothing is mapped yet. Falls back to the row's first cell (never null - a row only
    /// exists when the catalog has at least one weight column).
    /// </summary>
    private static MatrixCellViewModel DefaultCellForFast(
        SetMeta meta,
        Gender sectionGender,
        IReadOnlyList<MatrixColumnViewModel> columns,
        IReadOnlyList<MatrixCellViewModel> cells)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (meta.GetVariant(sectionGender, columns[i].Weight) is not null)
            {
                return cells[i];
            }
        }

        return cells[0];
    }

    private static MatrixCellViewModel DefaultCellFor(
        ArmorSet set,
        Gender sectionGender,
        IReadOnlyList<MatrixColumnViewModel> columns,
        IReadOnlyList<MatrixCellViewModel> cells)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (VariantFor(set, sectionGender, columns[i].Weight) is not null)
            {
                return cells[i];
            }
        }

        return cells[0];
    }

    public static string Text(WeightClass weight) => weight switch
    {
        WeightClass.Heavy => "Heavy",
        WeightClass.Light => "Light",
        WeightClass.Clothing => "Clothing",
        WeightClass.Any => "n/a",
        _ => "Unknown",
    };

    public static string Text(Gender gender) => gender switch
    {
        Gender.Female => "FEMALE ARMOR",
        Gender.Male => "MALE ARMOR",
        _ => "ARMOR",
    };

    private static IReadOnlyList<MatrixColumnViewModel> BuildColumns(Catalog catalog)
    {
        var present = catalog.Sets
            .SelectMany(s => s.Variants)
            .Select(v => v.Weight)
            .Distinct()
            .ToList();

        var ranked = new[] { WeightClass.Heavy, WeightClass.Light, WeightClass.Clothing, WeightClass.Any };
        var columns = ranked.Where(present.Contains)
                            .Select(w => new MatrixColumnViewModel(w))
                            .ToList();

        // Any weights not in the canonical set still surface (deterministic), appended by enum value.
        foreach (var extra in present.Where(p => !columns.Any(c => c.Weight == p)).OrderBy(p => p))
        {
            columns.Add(new MatrixColumnViewModel(extra));
        }

        return columns;
    }

    private static bool SetBelongsToSection(ArmorSet set, Gender sectionGender)
        => set.Variants.Any(v => v.Gender == sectionGender || v.Gender == Gender.Unisex);

    private static Variant? VariantFor(ArmorSet set, Gender sectionGender, WeightClass weight)
        => set.Variants.FirstOrDefault(v =>
            v.Weight == weight && (v.Gender == sectionGender || v.Gender == Gender.Unisex));

    private static MatrixCellViewModel BuildCellFast(
        ArmorSet set,
        Gender sectionGender,
        WeightClass weight,
        Variant? variant,
        Dictionary<(string SetId, string PieceId, Gender Gender), PieceMapping> mappingsByKey,
        Dictionary<string, List<PieceMapping>> mappingsBySet,
        Dictionary<Guid, DonorAsset> donorById,
        ArmorSetStatus status,
        bool isMatch)
    {
        if (variant is null)
        {
            return MatrixCellViewModel.Blank(set, sectionGender, weight, status, isMatch);
        }

        var setMappings = GetMappingsForVariantFast(set, variant, mappingsByKey, mappingsBySet);
        if (setMappings.Count == 0)
        {
            return MatrixCellViewModel.Blank(set, sectionGender, weight, status, isMatch);
        }

        var lines = BuildCardLinesFast(set, setMappings, donorById);
        return new MatrixCellViewModel(set, sectionGender, weight, variant, lines, status, isMatch);
    }

    private static MatrixCellViewModel BuildCell(
        ArmorSet set,
        Gender sectionGender,
        WeightClass weight,
        IReadOnlyList<PieceMapping> mappings,
        DonorLibraryModel library,
        ArmorSetStatus status,
        bool isMatch)
    {
        var variant = VariantFor(set, sectionGender, weight);
        if (variant is null)
        {
            return MatrixCellViewModel.Blank(set, sectionGender, weight, status, isMatch);
        }

        var setMappings = set.PiecesMappingsFor(variant, mappings);
        if (setMappings.Count == 0)
        {
            return MatrixCellViewModel.Blank(set, sectionGender, weight, status, isMatch);
        }

        var lines = BuildCardLines(set, setMappings, library);
        return new MatrixCellViewModel(set, sectionGender, weight, variant, lines, status, isMatch);
    }

    private static IReadOnlyList<CellLineViewModel> BuildCardLinesFast(
        ArmorSet set,
        IReadOnlyList<PieceMapping> setMappings,
        Dictionary<Guid, DonorAsset> donorById)
    {
        var lines = new List<CellLineViewModel> { new(set.DisplayName, CellLineRole.Set) };

        var donors = setMappings.Select(m => m.DonorAssetId).Distinct();
        foreach (var donorId in donors)
        {
            if (!donorById.TryGetValue(donorId, out var asset))
            {
                continue;
            }

            lines.Add(new CellLineViewModel(DonorDisplayName(asset), CellLineRole.Donor));
        }

        var body = setMappings.Select(m => m.BodyConversionPatchAssetId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        foreach (var patchId in body)
        {
            if (donorById.TryGetValue(patchId, out var asset))
            {
                lines.Add(new CellLineViewModel(DonorDisplayName(asset), CellLineRole.BodyPatch));
            }
        }

        var physics = setMappings.Select(m => m.PhysicsPatchAssetId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        foreach (var patchId in physics)
        {
            if (donorById.TryGetValue(patchId, out var asset))
            {
                lines.Add(new CellLineViewModel(DonorDisplayName(asset), CellLineRole.PhysicsPatch));
            }
        }

        return lines;
    }

    private static IReadOnlyList<CellLineViewModel> BuildCardLines(
        ArmorSet set,
        IReadOnlyList<PieceMapping> setMappings,
        DonorLibraryModel library)
    {
        var lines = new List<CellLineViewModel> { new(set.DisplayName, CellLineRole.Set) };

        var donors = setMappings.Select(m => m.DonorAssetId).Distinct();
        foreach (var donorId in donors)
        {
            var asset = library.Assets.FirstOrDefault(a => a.ImportId == donorId);
            if (asset is null)
            {
                continue;
            }

            lines.Add(new CellLineViewModel(DonorDisplayName(asset), CellLineRole.Donor));
        }

        var body = setMappings.Select(m => m.BodyConversionPatchAssetId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        foreach (var patchId in body)
        {
            var asset = library.Assets.FirstOrDefault(a => a.ImportId == patchId);
            if (asset is not null)
            {
                lines.Add(new CellLineViewModel(DonorDisplayName(asset), CellLineRole.BodyPatch));
            }
        }

        var physics = setMappings.Select(m => m.PhysicsPatchAssetId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        foreach (var patchId in physics)
        {
            var asset = library.Assets.FirstOrDefault(a => a.ImportId == patchId);
            if (asset is not null)
            {
                lines.Add(new CellLineViewModel(DonorDisplayName(asset), CellLineRole.PhysicsPatch));
            }
        }

        return lines;
    }

    private static string DonorDisplayName(DonorAsset asset)
        => asset.ProvidedSets.Count > 0
            ? asset.ProvidedSets[0].DisplayName
            : asset.OriginalFileName;

    private static List<PieceMapping> PiecesMappingsFor(this ArmorSet set, Variant variant, IReadOnlyList<PieceMapping> mappings)
    {
        var result = new List<PieceMapping>();
        foreach (var piece in variant.Pieces)
        {
            var match = mappings.FirstOrDefault(m =>
                m.TargetArmorSetId == set.Id
                && m.TargetPieceEditorId == piece.EditorId
                && (m.TargetGender == variant.Gender || variant.Gender == Gender.Unisex));
            if (match is not null)
            {
                result.Add(match);
            }
        }

        return result;
    }
}
