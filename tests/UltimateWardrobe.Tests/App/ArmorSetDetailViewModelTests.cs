using FluentAssertions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

namespace UltimateWardrobe.Tests.App;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

/// <summary>
/// Sprint 6.5 - Anchored single-cell mapping editor (<see cref="ArmorSetDetailViewModel"/> rescoped +
/// the popover open/close state machine on <see cref="OverhaulViewModel"/>), headless:
/// popover open/close with the correct variant/mapping payload and autosave flush on close,
/// the Phase 3 mapping command sequence determinism, the donor compatibility filter predicate,
/// the needs-patch row highlight under a strict patch policy, autosave invoking
/// <see cref="IProjectStore.SaveAsync"/> on every edit, per-op status refresh, and the matrix cell
/// card recompute observed by the host after each edit.
/// </summary>
[Trait("Category", "App")]
public class ArmorSetDetailViewModelTests
{
    [Fact]
    public void Donor_compatibility_filter_accepts_gender_weight_match_and_rejects_others()
    {
        var d = Fixtures.Create();
        var femaleHeavy = d.DonorFemaleHeavy;
        var maleHeavy = d.DonorMaleHeavy;

        DonorCompatibility.IsCompatible(femaleHeavy, Gender.Female, WeightClass.Heavy).Should().BeTrue();
        DonorCompatibility.IsCompatible(femaleHeavy, Gender.Male, WeightClass.Heavy).Should().BeFalse();
        DonorCompatibility.IsCompatible(maleHeavy, Gender.Male, WeightClass.Heavy).Should().BeTrue();
        DonorCompatibility.IsCompatible(maleHeavy, Gender.Female, WeightClass.Heavy).Should().BeFalse();
    }

    [Fact]
    public async Task Popover_open_payload_binds_the_cell_variant_and_close_resets()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        var cell = vm.CellAt(0, 0, 0); // (Iron, Female, Heavy)

        vm.Activate(cell!);

        vm.IsEditorOpen.Should().BeTrue();
        vm.ActiveCell.Should().BeSameAs(cell);
        vm.CellEditor.IsOpen.Should().BeTrue();
        vm.CellEditor.Set!.Id.Should().Be("IronArmor");
        vm.CellEditor.Variant!.Gender.Should().Be(Gender.Female);
        vm.CellEditor.Variant.Weight.Should().Be(WeightClass.Heavy);
        vm.CellEditor.Rows.Should().ContainSingle(r => r.EditorId == "IronCuirassF");

        await vm.FlushAndCloseEditorAsync();

        vm.IsEditorOpen.Should().BeFalse();
        vm.ActiveCell.Should().BeNull();
        vm.CellEditor.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Activate_same_cell_toggles_the_editor_closed()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        var cell = vm.CellAt(0, 0, 0);

        vm.Activate(cell!);
        vm.IsEditorOpen.Should().BeTrue();

        vm.Activate(cell!);
        vm.IsEditorOpen.Should().BeFalse("activating the already-open cell closes the popover");
        vm.ActiveCell.Should().BeNull();
    }

    [Fact]
    public void Assigning_a_donor_flows_through_the_phase3_command_sequence_to_mapped()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);

        var row = vm.CellEditor.Rows.Single();
        row.Status.Should().Be(MappingStatus.Pending);
        row.SelectedDonor = row.Donors.Single(o => o.Asset == d.DonorFemaleHeavy);

        vm.CellEditor.AssignDonor(row);

        d.Overhaul.Mappings.Should().ContainSingle();
        var cell = vm.CellAt(0, 0, 0)!;
        cell.IsBlank.Should().BeFalse();
        cell.Lines.Select(l => l.Text).Should().Contain("D1 Alpha", "grid card recompute observes the edit");
        vm.CellEditor.Rows.Single().Status.Should().Be(MappingStatus.Mapped);
    }

    [Fact]
    public void Needs_patch_row_highlights_and_offers_the_patch_panel_under_a_strict_policy()
    {
        var d = Fixtures.Create(policy: PatchPolicy.RequireBodyConversion);
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);

        var row = vm.CellEditor.Rows.Single();
        row.SelectedDonor = row.Donors.Single(o => o.Asset == d.DonorFemaleHeavyBodyOnly);
        vm.CellEditor.AssignDonor(row);

        var refreshed = vm.CellEditor.Rows.Single();
        refreshed.Status.Should().Be(MappingStatus.NeedsPatch);
        refreshed.IsNeedsPatch.Should().BeTrue();
        refreshed.ShowPatchPanel.Should().BeTrue();
        refreshed.BodyPatches.Should().ContainSingle(o => o.Asset == d.BodyPatch);
    }

    [Fact]
    public void Attaching_the_body_patch_resolves_the_needs_patch()
    {
        var d = Fixtures.Create(policy: PatchPolicy.RequireBodyConversion);
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);

        var row = vm.CellEditor.Rows.Single();
        row.SelectedDonor = row.Donors.Single(o => o.Asset == d.DonorFemaleHeavyBodyOnly);
        vm.CellEditor.AssignDonor(row);
        vm.CellEditor.Rows.Single().Status.Should().Be(MappingStatus.NeedsPatch);

        var needsPatch = vm.CellEditor.Rows.Single();
        needsPatch.SelectedBodyPatch = needsPatch.BodyPatches.Single(o => o.Asset == d.BodyPatch);
        vm.CellEditor.AttachBodyPatch(needsPatch);

        vm.CellEditor.Rows.Single().Status.Should().Be(MappingStatus.Mapped);
    }

    [Fact]
    public async Task Each_edit_flushes_the_autosave_through_the_session_store()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        var savesBefore = d.Store.SaveCount;
        vm.Activate(vm.CellAt(0, 0, 0)!);

        var row = vm.CellEditor.Rows.Single();
        row.SelectedDonor = row.Donors.Single(o => o.Asset == d.DonorFemaleHeavy);
        vm.CellEditor.AssignDonor(row);

        d.Store.SaveCount.Should().BeGreaterThan(savesBefore, "an edit flushes SaveAsync via the session store");

        var closes = d.Store.SaveCount;
        await vm.FlushAndCloseEditorAsync();
        d.Store.SaveCount.Should().BeGreaterThan(closes, "closing the popover performs a guaranteed flush");
    }

    [Fact]
    public void Per_op_status_refresh_recomputes_the_set_status_and_matrix()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.CellEditor.IsOpen.Should().BeFalse();

        vm.Activate(vm.CellAt(0, 0, 0)!);
        vm.CellEditor.SetStatus.Should().Be(ArmorSetStatus.NotStarted);

        var row = vm.CellEditor.Rows.Single();
        row.SelectedDonor = row.Donors.Single(o => o.Asset == d.DonorFemaleHeavy);
        vm.CellEditor.AssignDonor(row);

        vm.CellEditor.SetStatus.Should().Be(ArmorSetStatus.Mapped);
        vm.CellAt(0, 0, 0)!.Status.Should().Be(ArmorSetStatus.Mapped);
    }

    [Fact]
    public void Import_patch_shortcut_navigates_to_the_donor_library()
    {
        var d = Fixtures.Create();
        d.Vm.CellEditor.ImportPatch();

        d.Navigation.Navigated.Should().Contain(typeof(UltimateWardrobe.App.Views.DonorLibraryView));
    }

    private static class Fixtures
    {
        public static (OverhaulViewModel Vm, RecordingStore Store, Overhaul Overhaul, RecordingNavigation Navigation,
                       DonorAsset DonorFemaleHeavy, DonorAsset DonorMaleHeavy, DonorAsset DonorFemaleHeavyBodyOnly,
                       DonorAsset BodyPatch, DonorAsset PhysicsPatch) Create(PatchPolicy policy = PatchPolicy.Loose)
        {
            var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
            var library = new DonorLibraryModel(Guid.NewGuid());

            var donorFemaleHeavy = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "donor-f.7z", "C:/Src/df", DateTime.UtcNow, "hf",
                DonorAssetKind.FullReplacer,
                new[] { new DonorProvidedSet("dfset", "D1 Alpha", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("DFPiece", 0x11, "32 Body", "DFArma", "donor/df.nif") }) }) });

            var donorMaleHeavy = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "donor-m.7z", "C:/Src/dm", DateTime.UtcNow, "hm",
                DonorAssetKind.FullReplacer,
                new[] { new DonorProvidedSet("dmset", "D2 Male", new[] { new Variant(Gender.Male, WeightClass.Heavy, new[] { new Piece("DMPiece", 0x12, "32 Body", "DMArma", "donor/dm.nif") }) }) });

            var donorBodyOnly = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "donor-body.7z", "C:/Src/db", DateTime.UtcNow, "hb",
                DonorAssetKind.FullReplacer,
                new[] { new DonorProvidedSet("dbset", "D3 Body Only", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("DBPiece", 0x13, "32 Body", "DBArma", "donor/plain.nif") }) }) });

            var bodyPatch = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), "patch-body.7z", "C:/Src/pb", DateTime.UtcNow, "hp",
                DonorAssetKind.BodyConversionPatch, new[] { new DonorProvidedSet("pbset", "B1 Body") },
                detectedBodySlideFiles: new[] { "body/convert.nif" });

            var physicsPatch = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"), "patch-phys.7z", "C:/Src/pp", DateTime.UtcNow, "hq",
                DonorAssetKind.PhysicsPatch, new[] { new DonorProvidedSet("ppset", "P1 Phys") });

            foreach (var a in new[] { donorFemaleHeavy, donorMaleHeavy, donorBodyOnly, bodyPatch, physicsPatch })
            {
                library.Assets.Add(a);
                project.Library.Assets.Add(a);
            }

            var ironArmor = new ArmorSet("IronArmor", "Iron Armor",
                new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("IronCuirassF", 0x21, "32 Body", "IronCuirassFArma", "armor/IronCuirassF.nif") }) });

            var catalog = new Catalog(new VanillaCatalogSource("C:/Game"), new[] { ironArmor });

            var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game"))
            {
                Catalog = catalog,
                Policy = policy,
            };
            project.Overhauls.Add(overhaul);

            var session = new ProjectSession();
            var store = new RecordingStore();
            session.Open(project, "C:/Projects/Test/project.db", store);

            var selection = new OverhaulSelection();
            selection.Select(overhaul.Id);

            var navigation = new RecordingNavigation();
            var vm = new OverhaulViewModel(session, selection, new MappingService(library), navigation, new ScriptedDialogService());
            vm.Refresh();

            return (vm, store, overhaul, navigation, donorFemaleHeavy, donorMaleHeavy, donorBodyOnly, bodyPatch, physicsPatch);
        }
    }
}
