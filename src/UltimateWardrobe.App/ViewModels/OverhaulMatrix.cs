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
/// observable-collection matrix rebuild - 2-D virtualization constraint 4.5).
/// </summary>
public sealed class ArmorSetRowViewModel
{
    public ArmorSet Set { get; }
    public Gender SectionGender { get; }
    public string DisplayName => Set.DisplayName;
    public ArmorSetStatus Status { get; }
    public bool IsStatusMatch { get; }
    public IReadOnlyList<MatrixCellViewModel> Cells { get; }

    public ArmorSetRowViewModel(
        ArmorSet set,
        Gender sectionGender,
        ArmorSetStatus status,
        bool isStatusMatch,
        IReadOnlyList<MatrixCellViewModel> cells)
    {
        Set = set;
        SectionGender = sectionGender;
        Status = status;
        IsStatusMatch = isStatusMatch;
        Cells = cells;
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
/// </summary>
public sealed record OverhaulMatrixViewModel(
    IReadOnlyList<MatrixColumnViewModel> Columns,
    IReadOnlyList<MatrixSectionViewModel> Sections);

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

        var columns = BuildColumns(catalog);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var sections = new List<MatrixSectionViewModel>();
        foreach (var (sectionGender, _) in SectionOrder)
        {
            var rows = new List<ArmorSetRowViewModel>();
            foreach (var set in catalog.Sets)
            {
                if (!SetBelongsToSection(set, sectionGender))
                {
                    continue;
                }

                if (normalizedSearch is not null
                    && !set.DisplayName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var status = mapping.GetArmorSetStatus(set, mappings);
                var isMatch = statusFilter.HasValue && status == statusFilter.Value;
                var cells = columns.Select(col => BuildCell(set, sectionGender, col.Weight, mappings, library, status, isMatch))
                                   .ToList();
                rows.Add(new ArmorSetRowViewModel(set, sectionGender, status, isMatch, cells));
            }

            if (rows.Count > 0)
            {
                sections.Add(new MatrixSectionViewModel(sectionGender, rows));
            }
        }

        return new OverhaulMatrixViewModel(columns, sections);
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
