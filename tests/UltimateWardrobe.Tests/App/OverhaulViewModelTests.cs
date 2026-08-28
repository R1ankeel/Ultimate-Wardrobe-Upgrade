using FluentAssertions;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.4 - Overhaul mapping matrix (<see cref="OverhaulViewModel"/> + <see cref="OverhaulMatrix"/>),
/// headless: FEMALE/MALE section grouping and row ordering, weight-column projection (cell identity per
/// (set, gender, weight), missing weight class yields no column, n/a blanks), blank-cell correctness,
/// card line rendering (set + donor + one line per attached body/physics patch), search row-band
/// reduction, status-filter highlighting mapped onto <see cref="MappingService.GetArmorSetStatus"/>, and
/// cell-coordinate resolution feeding the popover anchor.
/// </summary>
[Trait("Category", "App")]
public class OverhaulViewModelTests
{
    [Fact]
    public void Sections_are_FEMALE_before_MALE_with_catalog_order_rows()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        vm.Sections.Should().HaveCount(2, "FEMALE then MALE");
        vm.Sections[0].Gender.Should().Be(Gender.Female);
        vm.Sections[0].Header.Should().Be("FEMALE ARMOR");
        vm.Sections[1].Gender.Should().Be(Gender.Male);
        vm.Sections[1].Header.Should().Be("MALE ARMOR");

        vm.Sections[0].Rows.Select(r => r.DisplayName).Should().BeEquivalentTo(
            new[] { "Iron Armor", "Leather Armor", "Cloth Robe", "Unisex Shawl", "Linen Armor" }, opt => opt.WithStrictOrdering());
        vm.Sections[1].Rows.Select(r => r.DisplayName).Should().BeEquivalentTo(
            new[] { "Iron Armor", "Leather Armor", "Unisex Shawl" }, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void Columns_are_the_distinct_catalog_weight_classes_in_order()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        vm.Columns.Select(c => c.Header).Should().BeEquivalentTo(
            new[] { "Heavy", "Light", "Clothing", "n/a" }, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void Missing_weight_class_produces_no_column()
    {
        var catalog = Fixtures.CreateSingleWeightCatalog(WeightClass.Heavy);
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        vm.Columns.Should().ContainSingle().Which.Weight.Should().Be(WeightClass.Heavy);
    }

    [Fact]
    public void Each_cell_identifies_its_set_section_gender_and_weight_and_missing_weight_is_blank()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var donors = Fixtures.CreateDonors();
        var mappings = new[]
        {
            Fixtures.CreateMapping(catalog, "IronArmor", "IronCuirassF", Gender.Female, donors.Donor),
        };
        var vm = Fixtures.BuildVm(catalog, mappings, donors.Library);

        // (Iron, Female, Heavy): the mapped cell carries the Female Heavy variant.
        var ironFemale = vm.CellAt(0, 0, 0); // section 0 FEMALE, row 0 = Iron, col 0 = Heavy
        ironFemale.Should().NotBeNull();
        ironFemale!.Set.DisplayName.Should().Be("Iron Armor");
        ironFemale.SectionGender.Should().Be(Gender.Female);
        ironFemale.Weight.Should().Be(WeightClass.Heavy);
        ironFemale.Variant.Should().NotBeNull();
        ironFemale.Variant!.Weight.Should().Be(WeightClass.Heavy);
        ironFemale.Variant!.Gender.Should().Be(Gender.Female);

        // Iron has no Clothing variant -> that column cell is a no-variant blank.
        var ironClothing = vm.CellAt(0, 0, 2);
        ironClothing!.Variant.Should().BeNull();
        ironClothing.IsBlank.Should().BeTrue();
        ironClothing.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Blank_cell_variant_no_mapping_is_empty()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        // Linen has a Female Light variant but no mappings -> Light cell is blank and empty.
        var linenRow = vm.Sections[0].Rows.Single(r => r.DisplayName == "Linen Armor");
        var linenLight = linenRow.Cells.Single(c => c.Weight == WeightClass.Light);
        linenLight.IsBlank.Should().BeTrue();
        linenLight.Lines.Should().BeEmpty();
        linenLight.Variant.Should().BeNull("an unmapped cell renders as an empty blank");
    }

    [Fact]
    public void Mapped_cell_renders_set_donor_and_one_line_per_attached_patch()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var donors = Fixtures.CreateDonors();
        var mappings = new[]
        {
            Fixtures.CreateMapping(
                catalog, "IronArmor", "IronCuirassF", Gender.Female, donors.Donor, donors.BodyPatch, donors.PhysicsPatch),
        };
        var vm = Fixtures.BuildVm(catalog, mappings, donors.Library);

        var cell = vm.CellAt(0, 0, 0); // (Iron, Female, Heavy)
        cell!.IsBlank.Should().BeFalse();
        cell.Lines.Select(l => l.Text).Should().BeEquivalentTo(
            new[] { "Iron Armor", "D1 Alpha", "P3 Body", "P4 Phys" }, opt => opt.WithStrictOrdering());
        cell.Lines.Select(l => l.Role).Should().BeEquivalentTo(
            new[]
            {
                CellLineRole.Set,
                CellLineRole.Donor,
                CellLineRole.BodyPatch,
                CellLineRole.PhysicsPatch,
            }, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void Search_reduces_rows_in_both_sections_case_insensitively()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        vm.SearchText = "iron";
        vm.Sections[0].Rows.Select(r => r.DisplayName).Should().BeEquivalentTo(
            new[] { "Iron Armor" }, opt => opt.WithStrictOrdering());
        vm.Sections[1].Rows.Select(r => r.DisplayName).Should().BeEquivalentTo(
            new[] { "Iron Armor" }, opt => opt.WithStrictOrdering());

        vm.SearchText = "robe";
        vm.Sections.Should().ContainSingle("the MALE section has no matching rows and is dropped");
        vm.Sections[0].Rows.Select(r => r.DisplayName).Should().BeEquivalentTo(
            new[] { "Cloth Robe" }, opt => opt.WithStrictOrdering());

        vm.SearchText = "";
        vm.Sections[0].Rows.Count.Should().Be(5, "clearing search restores all FEMALE rows");
        vm.Sections.Should().HaveCount(2, "clearing search restores the MALE section");
    }

    [Fact]
    public void Status_filter_highlights_only_matching_sets()
    {
        var catalog = Fixtures.CreateStatusCatalog();
        var mappings = Fixtures.CreateStatusMappings(catalog);
        var vm = Fixtures.BuildVm(catalog, mappings);

        vm.StatusFilter = ArmorSetStatus.Mapped;
        vm.Sections.Single().Rows.Single(r => r.Set.Id == "FullSet").IsStatusMatch.Should().BeTrue();
        vm.Sections.Single().Rows.Should().OnlyContain(r => r.Set.Id == "FullSet" ? r.IsStatusMatch : !r.IsStatusMatch);

        vm.StatusFilter = ArmorSetStatus.NeedsPatch;
        vm.Sections.Single().Rows.Single(r => r.Set.Id == "PatchSet").IsStatusMatch.Should().BeTrue();

        vm.StatusFilter = ArmorSetStatus.NotStarted;
        vm.Sections.Single().Rows.Single(r => r.Set.Id == "NoMapSet").IsStatusMatch.Should().BeTrue();
    }

    [Fact]
    public void CellAt_resolves_coordinates_and_returns_null_out_of_range()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        var cell = vm.CellAt(1, 0, 0); // MALE, Iron, Heavy
        cell.Should().NotBeNull();
        cell!.SectionGender.Should().Be(Gender.Male);
        cell.Set.DisplayName.Should().Be("Iron Armor");

        vm.CellAt(2, 0, 0).Should().BeNull("only two sections");
        vm.CellAt(0, 99, 0).Should().BeNull("row out of range");
        vm.CellAt(0, 0, 99).Should().BeNull("column out of range");
    }

    [Fact]
    public void MatrixItems_flatten_headers_and_rows_in_section_order()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        vm.MatrixItems.Should().HaveCount(10, "FEMALE header + 5 rows, MALE header + 3 rows");
        vm.MatrixItems[0].Should().BeOfType<MatrixSectionHeaderViewModel>().Which.Header.Should().Be("FEMALE ARMOR");
        vm.MatrixItems[1].Should().BeOfType<ArmorSetRowViewModel>().Which.DisplayName.Should().Be("Iron Armor");
        vm.MatrixItems[5].Should().BeOfType<ArmorSetRowViewModel>().Which.DisplayName.Should().Be("Linen Armor");
        vm.MatrixItems[6].Should().BeOfType<MatrixSectionHeaderViewModel>().Which.Header.Should().Be("MALE ARMOR");
        vm.MatrixItems[7].Should().BeOfType<ArmorSetRowViewModel>().Which.DisplayName.Should().Be("Iron Armor");
    }

    [Fact]
    public void Row_DefaultCell_is_the_first_weight_column_with_a_section_variant()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        // Iron (Female) carries Heavy -> the first editable coordinate is the Heavy column.
        var ironFemale = vm.Sections[0].Rows.Single(r => r.DisplayName == "Iron Armor");
        ironFemale.DefaultCell.Weight.Should().Be(WeightClass.Heavy);
        ironFemale.DefaultCell.Set.DisplayName.Should().Be("Iron Armor");

        // Cloth Robe (Female) has only a Clothing variant.
        var robe = vm.Sections[0].Rows.Single(r => r.DisplayName == "Cloth Robe");
        robe.DefaultCell.Weight.Should().Be(WeightClass.Clothing);

        // Linen (Female Light) is never mapped: the coordinate stays the default even though the cell
        // renders blank, so clicking the row name can still open the replacement editor (bug 6).
        var linen = vm.Sections[0].Rows.Single(r => r.DisplayName == "Linen Armor");
        linen.DefaultCell.Weight.Should().Be(WeightClass.Light);
        linen.DefaultCell.IsBlank.Should().BeTrue();
        linen.DefaultCell.Variant.Should().BeNull("unmapped cells stay blank but stay reachable via the row click");
    }

    [Fact]
    public void Activate_feeds_the_cell_for_the_popover_anchor()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, mappings: Array.Empty<PieceMapping>());

        var cell = vm.CellAt(0, 0, 0);
        vm.Activate(cell!);

        vm.ActiveCell.Should().BeSameAs(cell);
    }

    [Fact]
    public void Null_catalog_shows_empty_state()
    {
        var vm = Fixtures.BuildVmWithoutCatalog();

        vm.IsEmpty.Should().BeTrue();
        vm.HasCatalog.Should().BeFalse();
        vm.EmptyMessage.Should().Contain("scan");
        vm.Columns.Should().BeEmpty();
        vm.Sections.Should().BeEmpty();
    }

    private static class Fixtures
    {
        public static (DonorAsset Donor, DonorAsset BodyPatch, DonorAsset PhysicsPatch, UltimateWardrobe.Core.Domain.DonorLibrary Library) CreateDonors()
        {
            var donor = new DonorAsset(
                Guid.Parse("11111111-1111-1111-1111-111111111111"), "donor-alpha.7z", "C:/Src/d1", DateTime.UtcNow, "h1",
                DonorAssetKind.FullReplacer, new[] { new DonorProvidedSet("d1set", "D1 Alpha") });
            var body = new DonorAsset(
                Guid.Parse("22222222-2222-2222-2222-222222222222"), "patch-body.7z", "C:/Src/d2", DateTime.UtcNow, "h2",
                DonorAssetKind.BodyConversionPatch, new[] { new DonorProvidedSet("p3set", "P3 Body") });
            var physics = new DonorAsset(
                Guid.Parse("33333333-3333-3333-3333-333333333333"), "patch-phys.7z", "C:/Src/d3", DateTime.UtcNow, "h3",
                DonorAssetKind.PhysicsPatch, new[] { new DonorProvidedSet("p4set", "P4 Phys") });

            var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
            library.Assets.Add(donor);
            library.Assets.Add(body);
            library.Assets.Add(physics);

            return (donor, body, physics, library);
        }

        public static PieceMapping CreateMapping(
            Catalog catalog,
            string setEditorId,
            string pieceEditorId,
            Gender gender,
            DonorAsset donor,
            DonorAsset? body = null,
            DonorAsset? physics = null)
        {
            return new PieceMapping(
                Guid.NewGuid(),
                Guid.NewGuid(),
                setEditorId,
                pieceEditorId,
                gender,
                donor.ImportId,
                "DonorPiece",
                "donor/mesh.nif",
                body?.ImportId,
                physics?.ImportId,
                MappingStatus.Mapped);
        }

        private static Catalog NewCatalog(params ArmorSet[] sets)
            => new(new VanillaCatalogSource("C:/Game"), sets);

        private static ArmorSet Set(string id, string name, params Variant[] variants)
            => new(id, name, variants);

        private static Variant V(Gender gender, WeightClass weight, params Piece[] pieces)
            => new(gender, weight, pieces);

        private static Piece P(string editorId)
            => new(editorId, 0x12345678, "32 Body", editorId + "Arma", $"armor/{editorId}.nif");

        /// <summary>
        /// FEMALE-heavy catalog covering all four weight classes and several gender shapes: Iron
        /// (M/F heavy + F light), Leather (M/F light), Cloth Robe (F clothing only), Unisex Shawl
        /// (Unisex any), Linen (F light, never mapped). Also reused for the single-weight column test.
        /// </summary>
        public static Catalog CreateFullCatalog()
        {
            return NewCatalog(
                Set("IronArmor", "Iron Armor",
                    V(Gender.Male, WeightClass.Heavy, P("IronCuirassM"), P("IronGauntletsM")),
                    V(Gender.Female, WeightClass.Heavy, P("IronCuirassF"), P("IronGauntletsF")),
                    V(Gender.Female, WeightClass.Light, P("IronLightF"))),
                Set("LeatherArmor", "Leather Armor",
                    V(Gender.Male, WeightClass.Light, P("LthrM")),
                    V(Gender.Female, WeightClass.Light, P("LthrF"))),
                Set("ClothRobe", "Cloth Robe",
                    V(Gender.Female, WeightClass.Clothing, P("RobeF"))),
                Set("UnisexShawl", "Unisex Shawl",
                    V(Gender.Unisex, WeightClass.Any, P("ShawlU"))),
                Set("LinenArmor", "Linen Armor",
                    V(Gender.Female, WeightClass.Light, P("LinenF"))));
        }

        public static Catalog CreateSingleWeightCatalog(WeightClass weight)
            => NewCatalog(
                Set("SingleSet", "Single Set",
                    V(Gender.Female, weight, P("OnlyPiece"))));

        /// <summary>Four single-piece FEMALE sets, each forced to one stable status via mappings.</summary>
        public static Catalog CreateStatusCatalog()
        {
            return NewCatalog(
                Set("NoMapSet", "No Map Set", V(Gender.Female, WeightClass.Heavy, P("n0"))),
                Set("PartialSet", "Partial Set",
                    V(Gender.Female, WeightClass.Heavy, P("p0"), P("p1"))),
                Set("FullSet", "Full Set", V(Gender.Female, WeightClass.Heavy, P("f0"))),
                Set("PatchSet", "Patch Set", V(Gender.Female, WeightClass.Heavy, P("w0"))));
        }

        public static IReadOnlyList<PieceMapping> CreateStatusMappings(Catalog catalog)
        {
            var donor = new DonorAsset(Guid.NewGuid(), "donor.7z", "C:/Src/d", DateTime.UtcNow, "h", DonorAssetKind.FullReplacer,
                new[] { new DonorProvidedSet("s", "D") });
            var list = new List<PieceMapping>();

            // PartialSet: map only one of two female-heavy pieces -> InProgress.
            list.Add(new PieceMapping(Guid.NewGuid(), Guid.NewGuid(), "PartialSet", "p0", Gender.Female,
                donor.ImportId, "D", "d.nif", status: MappingStatus.Mapped));
            // FullSet: map the only piece -> Mapped.
            list.Add(new PieceMapping(Guid.NewGuid(), Guid.NewGuid(), "FullSet", "f0", Gender.Female,
                donor.ImportId, "D", "d.nif", status: MappingStatus.Mapped));
            // PatchSet: map the only piece but mark it NeedsPatch.
            list.Add(new PieceMapping(Guid.NewGuid(), Guid.NewGuid(), "PatchSet", "w0", Gender.Female,
                donor.ImportId, "D", "d.nif", status: MappingStatus.NeedsPatch));

            return list;
        }

        public static OverhaulViewModel BuildVm(
            Catalog catalog,
            IReadOnlyList<PieceMapping> mappings,
            UltimateWardrobe.Core.Domain.DonorLibrary? library = null)
        {
            var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
            if (library is not null)
            {
                foreach (var asset in library.Assets)
                {
                    project.Library.Assets.Add(asset);
                }
            }

            var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game"))
            {
                Catalog = catalog,
            };
            foreach (var m in mappings)
            {
                overhaul.Mappings.Add(m);
            }
            project.Overhauls.Add(overhaul);

            var session = new ProjectSession();
            var store = new RecordingStore();
            session.Open(project, "C:/Projects/Test/project.db", store);

            var selection = new OverhaulSelection();
            selection.Select(overhaul.Id);

            var vm = new OverhaulViewModel(session, selection, new MappingService(project.Library));
            vm.Refresh();
            return vm;
        }

        public static OverhaulViewModel BuildVmWithoutCatalog()
        {
            var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
            var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game"));
            project.Overhauls.Add(overhaul);

            var session = new ProjectSession();
            session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
            var selection = new OverhaulSelection();
            selection.Select(overhaul.Id);

            var vm = new OverhaulViewModel(session, selection, new MappingService(project.Library));
            vm.Refresh();
            return vm;
        }
    }
}
