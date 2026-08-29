using FluentAssertions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// E2 - Regression tests for filtering after C1-C6 + C4a.
/// Covers debounced async cancel, Columns stability, MatrixItems order after indexed rewrite,
/// donor-index golden compare, and cached-status in-place mutation.
/// </summary>
[Trait("Category", "App")]
public class OverhaulFilteringRegressionTests
{
    [Fact]
    public async Task Debounced_async_filter_cancels_previous_and_applies_last_search()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
        var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game")) { Catalog = catalog };
        project.Overhauls.Add(overhaul);

        var session = new ProjectSession();
        session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
        var selection = new OverhaulSelection();
        selection.Select(overhaul.Id);

        var background = new DispatcherBackgroundTaskService();
        var vm = new OverhaulViewModel(session, selection, new MappingService(project.Library), backgroundTasks: background);
        vm.Refresh();

        // Initial has both sections
        vm.Sections.Should().HaveCount(2);

        // Rapid changes: first "a" would match many, immediately overwritten by "iron" which matches 1 per section
        vm.SearchText = "a";
        vm.SearchText = "iron";

        // Wait for async filter (background Build + generation check) to settle.
        // Poll up to 2 seconds.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (vm.Sections.Count == 2
                && vm.Sections[0].Rows.Count == 1
                && vm.Sections[0].Rows[0].DisplayName == "Iron Armor"
                && vm.Sections[1].Rows.Count == 1)
            {
                break;
            }

            await Task.Delay(50);
        }

        vm.Sections[0].Rows.Select(r => r.DisplayName).Should().BeEquivalentTo(new[] { "Iron Armor" }, opt => opt.WithStrictOrdering());
        vm.Sections[1].Rows.Select(r => r.DisplayName).Should().BeEquivalentTo(new[] { "Iron Armor" }, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void Search_preserves_Columns_reference_when_catalog_unchanged()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, Array.Empty<PieceMapping>());

        var before = vm.Columns;
        before.Should().NotBeEmpty();

        vm.SearchText = "iron";

        var after = vm.Columns;
        after.Should().BeSameAs(before, "Columns depend only on Catalog, not on search - Phase B2/C2 must keep reference when catalog unchanged");

        vm.SearchText = "";
        vm.Columns.Should().BeSameAs(before);

        vm.SearchText = "robe";
        vm.Columns.Should().BeSameAs(before);
    }

    [Fact]
    public async Task Search_preserves_Columns_reference_with_background_service()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
        var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game")) { Catalog = catalog };
        project.Overhauls.Add(overhaul);
        var session = new ProjectSession();
        session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
        var selection = new OverhaulSelection();
        selection.Select(overhaul.Id);
        var background = new DispatcherBackgroundTaskService();
        var vm = new OverhaulViewModel(session, selection, new MappingService(project.Library), backgroundTasks: background);
        vm.Refresh();

        var before = vm.Columns;
        vm.SearchText = "iron";

        // Wait for async
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline && vm.Sections.Count != 1)
        {
            await Task.Delay(20);
        }

        vm.Columns.Should().BeSameAs(before);
    }

    [Fact]
    public void MatrixItems_order_still_FEMALE_header_then_rows_then_MALE_header_after_indexed_rewrite()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var vm = Fixtures.BuildVm(catalog, Array.Empty<PieceMapping>());

        vm.MatrixItems.Should().HaveCount(10);
        vm.MatrixItems[0].Should().BeOfType<MatrixSectionHeaderViewModel>().Which.Header.Should().Be("FEMALE ARMOR");
        vm.MatrixItems[1].Should().BeOfType<ArmorSetRowViewModel>().Which.DisplayName.Should().Be("Iron Armor");
        vm.MatrixItems[6].Should().BeOfType<MatrixSectionHeaderViewModel>().Which.Header.Should().Be("MALE ARMOR");
        vm.MatrixItems[7].Should().BeOfType<ArmorSetRowViewModel>().Which.DisplayName.Should().Be("Iron Armor");
    }

    [Fact]
    public void Donor_index_path_produces_same_cell_lines_as_expected_golden()
    {
        var catalog = Fixtures.CreateFullCatalog();
        var donors = Fixtures.CreateDonors();
        var mappings = new[]
        {
            Fixtures.CreateMapping(catalog, "IronArmor", "IronCuirassF", Gender.Female, donors.Donor, donors.BodyPatch, donors.PhysicsPatch),
        };
        var vm = Fixtures.BuildVm(catalog, mappings, donors.Library);

        var cell = vm.CellAt(0, 0, 0);
        cell!.IsBlank.Should().BeFalse();
        cell.Lines.Select(l => l.Text).Should().BeEquivalentTo(new[] { "Iron Armor", "D1 Alpha", "P3 Body", "P4 Phys" }, opt => opt.WithStrictOrdering());
        cell.Lines.Select(l => l.Role).Should().BeEquivalentTo(new[] { CellLineRole.Set, CellLineRole.Donor, CellLineRole.BodyPatch, CellLineRole.PhysicsPatch }, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void Cached_status_regression_in_place_mutation_reflects_new_mapping_same_list_reference()
    {
        // Warm catalog cache via first Build (OverhaulMatrix caches columns + SetMeta per Catalog)
        var catalog = new Catalog(new VanillaCatalogSource("C:/Game"), new[]
        {
            new ArmorSet("IronArmor", "Iron Armor", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("IronCuirassF", 0x21, "32 Body", "IronCuirassFArma", "armor/IronCuirassF.nif") }) }),
            new ArmorSet("LeatherArmor", "Leather Armor", new[] { new Variant(Gender.Female, WeightClass.Light, new[] { new Piece("LthrF", 0x22, "32 Body", "LthrFArma", "armor/LthrF.nif") }) }),
        });

        var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
        var donor = new DonorAsset(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "donor.7z", "C:/Src/d", DateTime.UtcNow, "h", DonorAssetKind.FullReplacer,
            new[] { new DonorProvidedSet("ds", "D", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("DonorPiece", 0x99, "32 Body", "DonorArma", "donor/mesh.nif") }) }) });
        project.Library.Assets.Add(donor);

        var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game")) { Catalog = catalog };
        project.Overhauls.Add(overhaul);

        var session = new ProjectSession();
        session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
        var selection = new OverhaulSelection();
        selection.Select(overhaul.Id);

        var mappingService = new MappingService(project.Library);
        var vm = new OverhaulViewModel(session, selection, mappingService);
        vm.Refresh();

        // Initially no mappings: Iron set is NotStarted, cell is blank, ProgressLabel shows 0 mapped
        vm.Sections[0].Rows.First(r => r.Set.Id == "IronArmor").Status.Should().Be(ArmorSetStatus.NotStarted);
        vm.MatrixItems.OfType<ArmorSetRowViewModel>().First(r => r.Set.Id == "IronArmor").Cells.First(c => c.Weight == WeightClass.Heavy).IsBlank.Should().BeTrue();
        vm.ProgressLabel.Should().Contain("0 mapped");

        // Capture mappings list reference before mutation
        var mappingsRef = overhaul.Mappings;
        mappingsRef.Should().BeEmpty();

        // Mutate Mappings in place via MappingService on the same Overhaul instance (no list replacement)
        var targetPiece = catalog.Sets.First(s => s.Id == "IronArmor").Variants.First().Pieces.First();
        var donorPiece = donor.ProvidedSets.First().Variants.First().Pieces.First();
        mappingService.AssignDonor(overhaul, catalog, donor, targetPiece, donorPiece);

        // Reference must be same object after in-place mutation - this is the hazard for ReferenceEquals cache
        ReferenceEquals(overhaul.Mappings, mappingsRef).Should().BeTrue("Overhaul.Mappings is mutated in place, reference does not change");
        overhaul.Mappings.Should().ContainSingle();

        // Warm status cache was NotStarted; next Build must reflect new Mapped status, not stale cache
        vm.Refresh();

        vm.Sections[0].Rows.First(r => r.Set.Id == "IronArmor").Status.Should().Be(ArmorSetStatus.Mapped, "in-place AssignDonor must be visible - stale ReferenceEquals cache would still report NotStarted");
        var cell = vm.MatrixItems.OfType<ArmorSetRowViewModel>().First(r => r.Set.Id == "IronArmor").Cells.First(c => c.Weight == WeightClass.Heavy);
        cell.IsBlank.Should().BeFalse("cell must now be mapped after in-place mutation");
        cell.Lines.Should().Contain(l => l.Role == CellLineRole.Donor);
        vm.ProgressLabel.Should().Contain("1 mapped");
    }

    [Fact]
    public void In_place_mutation_same_list_reference_must_not_return_stale_status_via_ReferenceEquals()
    {
        // Explicit test that would fail if status cache used ReferenceEquals(mappings, _cachedMappings) only
        var catalog = new Catalog(new VanillaCatalogSource("C:/Game"), new[]
        {
            new ArmorSet("SetA", "Set A", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("PieceA", 0x1, "32 Body", "ArmaA", "a.nif") }) }),
        });
        var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
        var donor = new DonorAsset(Guid.NewGuid(), "donor.7z", "C:/Src/d", DateTime.UtcNow, "h", DonorAssetKind.FullReplacer,
            new[] { new DonorProvidedSet("ds", "D", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("DonorPiece", 0x2, "32 Body", "DonorArma", "donor/mesh.nif") }) }) });
        project.Library.Assets.Add(donor);
        var overhaul = new Overhaul(Guid.NewGuid(), "O", project.Id, new VanillaCatalogSource("C:/Game")) { Catalog = catalog };
        project.Overhauls.Add(overhaul);

        var session = new ProjectSession();
        session.Open(project, "C:/Projects/Test/project.db", new RecordingStore());
        var selection = new OverhaulSelection();
        selection.Select(overhaul.Id);
        var mappingService = new MappingService(project.Library);
        var vm = new OverhaulViewModel(session, selection, mappingService);
        vm.Refresh();
        vm.Sections[0].Rows[0].Status.Should().Be(ArmorSetStatus.NotStarted);

        var refBefore = overhaul.Mappings;
        var piece = catalog.Sets[0].Variants[0].Pieces[0];
        var donorPiece = donor.ProvidedSets[0].Variants[0].Pieces[0];
        mappingService.AssignDonor(overhaul, catalog, donor, piece, donorPiece);
        (ReferenceEquals(overhaul.Mappings, refBefore)).Should().BeTrue();

        // Rebuild with same list instance - must show Mapped
        vm.Refresh();
        vm.Sections[0].Rows[0].Status.Should().Be(ArmorSetStatus.Mapped);
    }

    private static class Fixtures
    {
        public static (DonorAsset Donor, DonorAsset BodyPatch, DonorAsset PhysicsPatch, DonorLibraryModel Library) CreateDonors()
        {
            var donor = new DonorAsset(Guid.Parse("11111111-1111-1111-1111-111111111111"), "donor-alpha.7z", "C:/Src/d1", DateTime.UtcNow, "h1", DonorAssetKind.FullReplacer, new[] { new DonorProvidedSet("d1set", "D1 Alpha") });
            var body = new DonorAsset(Guid.Parse("22222222-2222-2222-2222-222222222222"), "patch-body.7z", "C:/Src/d2", DateTime.UtcNow, "h2", DonorAssetKind.BodyConversionPatch, new[] { new DonorProvidedSet("p3set", "P3 Body") });
            var physics = new DonorAsset(Guid.Parse("33333333-3333-3333-3333-333333333333"), "patch-phys.7z", "C:/Src/d3", DateTime.UtcNow, "h3", DonorAssetKind.PhysicsPatch, new[] { new DonorProvidedSet("p4set", "P4 Phys") });
            var library = new DonorLibraryModel(Guid.NewGuid());
            library.Assets.Add(donor);
            library.Assets.Add(body);
            library.Assets.Add(physics);
            return (donor, body, physics, library);
        }

        public static PieceMapping CreateMapping(Catalog catalog, string setEditorId, string pieceEditorId, Gender gender, DonorAsset donor, DonorAsset? body = null, DonorAsset? physics = null)
        {
            return new PieceMapping(Guid.NewGuid(), Guid.NewGuid(), setEditorId, pieceEditorId, gender, donor.ImportId, "DonorPiece", "donor/mesh.nif", body?.ImportId, physics?.ImportId, MappingStatus.Mapped);
        }

        private static Catalog NewCatalog(params ArmorSet[] sets) => new(new VanillaCatalogSource("C:/Game"), sets);
        private static ArmorSet Set(string id, string name, params Variant[] variants) => new(id, name, variants);
        private static Variant V(Gender gender, WeightClass weight, params Piece[] pieces) => new(gender, weight, pieces);
        private static Piece P(string editorId) => new(editorId, 0x12345678, "32 Body", editorId + "Arma", $"armor/{editorId}.nif");

        public static Catalog CreateFullCatalog()
        {
            return NewCatalog(
                Set("IronArmor", "Iron Armor", V(Gender.Male, WeightClass.Heavy, P("IronCuirassM"), P("IronGauntletsM")), V(Gender.Female, WeightClass.Heavy, P("IronCuirassF"), P("IronGauntletsF")), V(Gender.Female, WeightClass.Light, P("IronLightF"))),
                Set("LeatherArmor", "Leather Armor", V(Gender.Male, WeightClass.Light, P("LthrM")), V(Gender.Female, WeightClass.Light, P("LthrF"))),
                Set("ClothRobe", "Cloth Robe", V(Gender.Female, WeightClass.Clothing, P("RobeF"))),
                Set("UnisexShawl", "Unisex Shawl", V(Gender.Unisex, WeightClass.Any, P("ShawlU"))),
                Set("LinenArmor", "Linen Armor", V(Gender.Female, WeightClass.Light, P("LinenF"))));
        }

        public static OverhaulViewModel BuildVm(Catalog catalog, IReadOnlyList<PieceMapping> mappings, DonorLibraryModel? library = null)
        {
            var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
            if (library is not null) foreach (var asset in library.Assets) project.Library.Assets.Add(asset);
            var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game")) { Catalog = catalog };
            foreach (var m in mappings) overhaul.Mappings.Add(m);
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
