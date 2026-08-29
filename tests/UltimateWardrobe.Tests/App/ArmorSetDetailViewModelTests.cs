using FluentAssertions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

/// <summary>
/// Sprint 6.9 T2 - SET-level replacement editor (<see cref="ArmorSetDetailViewModel"/> rescoped), headless:
/// the LArmor/Load-Armor empty state, the ARMOR 2 body/physics checkmarks vs the "Load .. patch" rows
/// driven by the REAL donor detection flags, the replacement-gender body requirement (female -> 3BA,
/// male -> HIMBO), the donor-library accounting on Change (a replaced donor is unloaded once nothing
/// references it, and stays while another set still does), the set-level donor/patch fan-out to every
/// variant piece with <see cref="MappingService.GetArmorSetStatus"/> /
/// <see cref="MappingService.GetOverhaulProgress"/> correct, autosave flushing on every edit via
/// <see cref="IProjectStore.SaveAsync"/>, and the matrix cell card recompute observed by the host after
/// each edit.
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
    public async Task Open_close_binds_the_variant_payload_and_resets()
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
    public void Load_armor_empty_state_shows_the_picker_and_no_checks()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);

        var editor = vm.CellEditor;
        editor.IsOpen.Should().BeTrue();
        editor.HasCurrentDonor.Should().BeFalse();
        editor.CurrentDonorText.Should().Be("Nothing loaded yet");
        editor.LoadDonorLabel.Should().Be("Load Armor");
        editor.RequiredBodyName.Should().Be("3BA");
        editor.BodyCheckText.Should().BeEmpty();
        editor.PhysicsCheckText.Should().BeEmpty();
        editor.ShowBodyPatchRow.Should().BeFalse();
        editor.ShowPhysicsPatchRow.Should().BeFalse();

        editor.Rows.Should().ContainSingle(r => r.EditorId == "IronCuirassF");
        editor.Rows.Single().Status.Should().Be(MappingStatus.Pending);
        editor.AvailableDonors.Should().Contain(o => o.Asset == d.DonorFemaleHeavy);
        editor.AvailableDonors.Should().NotContain(o => o.Asset.Kind != DonorAssetKind.FullReplacer);
    }

    [Fact]
    public void Donor_with_flags_shows_checkmarks_and_no_patch_rows()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.LoadDonor(d.DonorFemaleHeavy);

        d.Overhaul.Mappings.Should().ContainSingle();
        editor.HasCurrentDonor.Should().BeTrue();
        editor.CurrentDonorName.Should().Be("D1 Alpha");
        editor.CurrentDonorText.Should().Be("Armor: D1 Alpha");
        editor.LoadDonorLabel.Should().Be("Change");
        editor.HasRequiredBody.Should().BeTrue();
        editor.HasPhysics.Should().BeTrue();
        editor.BodyCheckText.Should().Be("3BA: OK");
        editor.PhysicsCheckText.Should().Be("HDT-SMP: OK");
        editor.ShowBodyPatchRow.Should().BeFalse();
        editor.ShowPhysicsPatchRow.Should().BeFalse();

        editor.Rows.Single().Status.Should().Be(MappingStatus.Mapped);
        editor.Rows.Single().HasBodySlide.Should().BeTrue();
        editor.Rows.Single().HasPhysics.Should().BeTrue();
        editor.SetStatus.Should().Be(ArmorSetStatus.Mapped);
        var cell = vm.CellAt(0, 0, 0)!;
        cell.IsBlank.Should().BeFalse();
        cell.Lines.Select(l => l.Text).Should().Contain("D1 Alpha", "grid card recompute observes the edit");
    }

    [Fact]
    public void Donor_without_flags_offers_the_body_and_physics_patch_rows()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.LoadDonor(d.DonorBodyOnly);

        editor.HasCurrentDonor.Should().BeTrue();
        editor.HasRequiredBody.Should().BeFalse();
        editor.HasPhysics.Should().BeFalse();
        editor.BodyCheckText.Should().Be("3BA: patch required");
        editor.PhysicsCheckText.Should().Be("HDT-SMP: patch required");
        editor.ShowBodyPatchRow.Should().BeTrue();
        editor.ShowPhysicsPatchRow.Should().BeTrue();
        editor.BodyPatches.Should().ContainSingle(o => o.Asset == d.BodyPatch);
        editor.PhysicsPatches.Should().ContainSingle(o => o.Asset == d.PhysicsPatch);
    }

    [Fact]
    public void Female_replacement_requires_3ba_and_male_requires_himbo()
    {
        var d = Fixtures.Create(includeMaleSet: true);
        var vm = d.Vm;

        vm.Activate(vm.CellAt(0, 0, 0)!); // Iron female
        vm.CellEditor.RequiredBodyName.Should().Be("3BA");
        vm.CellEditor.AvailableDonors.Should().Contain(o => o.Asset == d.DonorFemaleHeavy);
        vm.CellEditor.AvailableDonors.Should().NotContain(o => o.Asset == d.DonorMaleHeavy);

        vm.Activate(vm.CellAt(1, 0, 0)!); // Steel male
        var maleEditor = vm.CellEditor;
        maleEditor.Set!.Id.Should().Be("SteelArmor");
        maleEditor.RequiredBodyName.Should().Be("HIMBO");
        maleEditor.AvailableDonors.Should().Contain(o => o.Asset == d.DonorMaleHeavy);
        maleEditor.AvailableDonors.Should().NotContain(o => o.Asset == d.DonorFemaleHeavy);

        maleEditor.LoadDonor(d.DonorMaleHeavy);
        maleEditor.HasRequiredBody.Should().BeTrue("the male donor's mesh carries the himbo marker");
        maleEditor.BodyCheckText.Should().Be("HIMBO: OK");
        maleEditor.HasPhysics.Should().BeFalse();
        maleEditor.ShowPhysicsPatchRow.Should().BeTrue();
        maleEditor.Rows.Single().DonorBodyMarkerText.Should().Be(BodyType.HIMBO.ToString());
    }

    [Fact]
    public void Change_donor_unloads_the_old_donor_when_nothing_else_references_it()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.LoadDonor(d.DonorFemaleHeavy);
        d.Library.Assets.Should().Contain(d.DonorFemaleHeavy);

        editor.LoadDonor(d.DonorBodyOnly);

        d.Library.Assets.Should().NotContain(d.DonorFemaleHeavy, "the replaced donor is unloaded once nothing references it");
        d.Library.Assets.Should().Contain(d.DonorBodyOnly);
        var mapping = d.Overhaul.Mappings.Should().ContainSingle().Subject;
        mapping.DonorAssetId.Should().Be(d.DonorBodyOnly.ImportId);
        mapping.BodyConversionPatchAssetId.Should().BeNull();
        editor.LoadDonorLabel.Should().Be("Change");
        vm.CellAt(0, 0, 0)!.Lines.Select(l => l.Text).Should().Contain("D3 Body Only");
    }

    [Fact]
    public void Change_donor_keeps_the_donor_while_another_set_still_references_it()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.LoadDonor(d.DonorFemaleHeavy);

        // Another set's mapping still references the donor.
        d.Overhaul.Mappings.Add(new PieceMapping(
            Guid.NewGuid(), d.Overhaul.Id, "SteelArmor", "SteelCuirassM", Gender.Male,
            d.DonorFemaleHeavy.ImportId, "DFPiece", "donor/df.nif", status: MappingStatus.Mapped));

        editor.LoadDonor(d.DonorBodyOnly);

        d.Library.Assets.Should().Contain(d.DonorFemaleHeavy, "a donor a referenced set still uses stays in the library");
        d.Overhaul.Mappings.Should().Contain(m => m.DonorAssetId == d.DonorBodyOnly.ImportId);
    }

    [Fact]
    public void Set_level_assign_fans_out_to_every_piece_with_correct_status_and_progress()
    {
        var d = Fixtures.Create(ironFemalePieces: new[]
        {
            new Piece("IronCuirassF", 0x21, "32 Body", "IronCuirassFArma", "armor/IronCuirassF.nif"),
            new Piece("IronGauntletsF", 0x22, "34 Gauntlets", "IronGauntletsFArma", "armor/IronGauntletsF.nif"),
        });
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.LoadDonor(d.DonorFemaleHeavy);

        d.Overhaul.Mappings.Should().HaveCount(2);
        d.Overhaul.Mappings.Should().OnlyContain(m => m.DonorAssetId == d.DonorFemaleHeavy.ImportId);
        editor.Rows.Should().HaveCount(2);
        editor.Rows.Should().OnlyContain(r => r.IsAssigned);

        editor.SetStatus.Should().Be(ArmorSetStatus.Mapped);
        var progress = new MappingService(d.Library).GetOverhaulProgress(d.Overhaul.Mappings, d.Overhaul.Catalog!);
        progress.TotalSets.Should().Be(1);
        progress.Mapped.Should().Be(1);
        progress.Done.Should().Be(0);
    }

    [Fact]
    public void Attaching_the_body_patch_fans_out_and_resolves_needs_patch_under_a_strict_policy()
    {
        var d = Fixtures.Create(policy: PatchPolicy.RequireBodyConversion, ironFemalePieces: new[]
        {
            new Piece("IronCuirassF", 0x21, "32 Body", "IronCuirassFArma", "armor/IronCuirassF.nif"),
            new Piece("IronGauntletsF", 0x22, "34 Gauntlets", "IronGauntletsFArma", "armor/IronGauntletsF.nif"),
        });
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.LoadDonor(d.DonorBodyOnly);
        editor.Rows.Should().OnlyContain(r => r.Status == MappingStatus.NeedsPatch);
        editor.ShowBodyPatchRow.Should().BeTrue();

        editor.LoadBodyPatch(d.BodyPatch);

        d.Overhaul.Mappings.Should().OnlyContain(m => m.BodyConversionPatchAssetId == d.BodyPatch.ImportId);
        editor.HasAttachedBodyPatch.Should().BeTrue();
        editor.ShowBodyPatchRow.Should().BeFalse("a body patch is already attached");
        editor.ShowClearBodyPatch.Should().BeTrue();
        editor.Rows.Should().OnlyContain(r => r.Status == MappingStatus.Mapped);
        editor.SetStatus.Should().Be(ArmorSetStatus.Mapped);
    }

    [Fact]
    public async Task Each_edit_flushes_the_autosave_through_the_session_store()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        var savesBefore = d.Store.SaveCount;
        vm.Activate(vm.CellAt(0, 0, 0)!);

        vm.CellEditor.LoadDonor(d.DonorFemaleHeavy);

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

        vm.CellEditor.LoadDonor(d.DonorFemaleHeavy);

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

    [Fact]
    public async Task LoadArmor_command_with_selection_assigns_the_selected_donor()
    {
        var d = Fixtures.Create();
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.SelectedDonor = editor.AvailableDonors.Single(o => o.Asset == d.DonorFemaleHeavy);

        await vm.CellEditor.LoadDonorCommand.ExecuteAsync(null);

        d.Overhaul.Mappings.Should().ContainSingle(m => m.DonorAssetId == d.DonorFemaleHeavy.ImportId);
        editor.HasCurrentDonor.Should().BeTrue();
        editor.LoadDonorLabel.Should().Be("Change");
    }

    [Fact]
    public async Task LoadArmor_without_selection_picks_and_imports_a_donor_archive()
    {
        var root = TestHelpers.NewTempDir("UW_Editor_");
        try
        {
            var archive = Path.Combine(root, "mod-donor.7z");
            File.WriteAllText(archive, "fake");

            var import = new ScriptedDonorImportRunner
            {
                OnImport = static (paths, root, library, _, _, _) =>
                {
                    var asset = new DonorAsset(
                        Guid.NewGuid(),
                        Path.GetFileName(paths[0]),
                        root,
                        DateTime.UtcNow,
                        "hash-imported",
                        DonorAssetKind.FullReplacer,
                        new[]
                        {
                            new DonorProvidedSet("importedset", "Imported Donor",
                                new[] { new Variant(Gender.Female, WeightClass.Heavy, new[]
                                {
                                    new Piece("IPiece", 0x11, "32 Body", "IArma", "donor/imported.nif"),
                                }) }),
                        });
                    library.Assets.Add(asset);
                    return Task.FromResult<IReadOnlyList<DonorAsset>>(new[] { asset });
                },
            };
            var catalog = new Catalog(new VanillaCatalogSource("C:/Game"), new[] { new ArmorSet("IronArmor", "Iron Armor",
                new[] { new Variant(Gender.Female, WeightClass.Heavy, new[]
                {
                    new Piece("IronCuirassF", 0x21, "32 Body", "IronCuirassFArma", "armor/IronCuirassF.nif"),
                }) }) });
            var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
            var library = project.Library;
            var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game"))
            {
                Catalog = catalog,
            };
            project.Overhauls.Add(overhaul);

            var store = new RecordingStore();
            var session = new ProjectSession();
            session.Open(project, "C:/Projects/Test/project.db", store);
            var selection = new OverhaulSelection();
            selection.Select(overhaul.Id);

            var dialogs = new ScriptedDialogService { PickModArchive = _ => archive };
            var vm = new OverhaulViewModel(
                session, selection, new MappingService(library), new RecordingNavigation(), dialogs, import);
            vm.Refresh();

            vm.Activate(vm.CellAt(0, 0, 0)!);
            vm.CellEditor.HasCurrentDonor.Should().BeFalse();
            vm.CellEditor.SelectedDonor.Should().BeNull();

            await vm.CellEditor.LoadDonorCommand.ExecuteAsync(null);

            import.Calls.Should().ContainSingle(c => c.Paths.SequenceEqual(new[] { archive }));
            library.Assets.Should().Contain(a => a.OriginalFileName == "mod-donor.7z");
            vm.CellEditor.HasCurrentDonor.Should().BeTrue("the imported donor is assigned to the variant in one step");
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task LoadArmor_cancelled_picker_does_nothing()
    {
        var root = TestHelpers.NewTempDir("UW_Editor_");
        try
        {
            var import = new ScriptedDonorImportRunner();
            var catalog = new Catalog(new VanillaCatalogSource("C:/Game"), new[] { new ArmorSet("IronArmor", "Iron Armor",
                new[] { new Variant(Gender.Female, WeightClass.Heavy, new[]
                {
                    new Piece("IronCuirassF", 0x21, "32 Body", "IronCuirassFArma", "armor/IronCuirassF.nif"),
                }) }) });
            var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
            var library = project.Library;
            var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game"))
            {
                Catalog = catalog,
            };
            project.Overhauls.Add(overhaul);

            var store = new RecordingStore();
            var session = new ProjectSession();
            session.Open(project, "C:/Projects/Test/project.db", store);
            var selection = new OverhaulSelection();
            selection.Select(overhaul.Id);

            var dialogs = new ScriptedDialogService(); // PickModArchive defaults to null (cancel)
            var vm = new OverhaulViewModel(
                session, selection, new MappingService(library), new RecordingNavigation(), dialogs, import);
            vm.Refresh();

            vm.Activate(vm.CellAt(0, 0, 0)!);

            await vm.CellEditor.LoadDonorCommand.ExecuteAsync(null);

            import.Calls.Should().BeEmpty("a cancelled picker never imports");
            vm.CellEditor.HasCurrentDonor.Should().BeFalse("no picker selection means no import and no assignment");
            overhaul.Mappings.Should().BeEmpty();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public void Clothing_one_piece_variant_mapped_by_Heavy_donor_body_is_Mapped()
    {
        // F6 - 1-piece Clothing variant (e.g. Belted Tunic, 32 Body) must be replaceable by a Heavy donor Body piece
        var d = Fixtures.Create(ironFemalePieces: new[]
        {
            new Piece("ClothesBeltedTunic", 0x5001, "32 Body", "ClothesBeltedTunicArma", "meshes/clothes/beltedtunic.nif"),
        });
        var vm = d.Vm;
        vm.Activate(vm.CellAt(0, 0, 0)!);
        var editor = vm.CellEditor;

        editor.Variant!.Pieces.Should().ContainSingle(p => p.EditorId == "ClothesBeltedTunic");
        // Heavy donor must appear in picker even for Clothing target (weight-agnostic, gender-only)
        editor.AvailableDonors.Should().Contain(o => o.Asset == d.DonorFemaleHeavy, "Heavy donor must be compatible with Clothing target (F1)");
        editor.AvailableDonors.Should().Contain(o => o.Asset == d.DonorBodyOnly);

        editor.LoadDonor(d.DonorFemaleHeavy);

        d.Overhaul.Mappings.Should().ContainSingle();
        editor.SetStatus.Should().Be(ArmorSetStatus.Mapped);
        var progress = new MappingService(d.Library).GetOverhaulProgress(d.Overhaul.Mappings, d.Overhaul.Catalog!);
        progress.Mapped.Should().Be(1);
        progress.Done.Should().Be(0);
        editor.HasRequiredBody.Should().BeTrue("Heavy donor carries 3ba token");
        editor.HasPhysics.Should().BeTrue();
        // Patch rows gender-correct: Female -> 3BA, not HIMBO
        editor.RequiredBodyName.Should().Be("3BA");
        editor.BodyCheckText.Should().Be("3BA: OK");
        editor.PhysicsCheckText.Should().Be("HDT-SMP: OK");
    }

    private sealed class Fixture
    {
        public required OverhaulViewModel Vm { get; init; }
        public required RecordingStore Store { get; init; }
        public required Overhaul Overhaul { get; init; }
        public required Project Project { get; init; }
        public required DonorLibraryModel Library { get; init; }
        public required RecordingNavigation Navigation { get; init; }
        public required DonorAsset DonorFemaleHeavy { get; init; }
        public required DonorAsset DonorMaleHeavy { get; init; }
        public required DonorAsset DonorBodyOnly { get; init; }
        public required DonorAsset BodyPatch { get; init; }
        public required DonorAsset PhysicsPatch { get; init; }
    }

    private static class Fixtures
    {
        public static Fixture Create(
            PatchPolicy policy = PatchPolicy.Loose,
            bool includeMaleSet = false,
            IReadOnlyList<Piece>? ironFemalePieces = null,
            IDonorImportRunner? importRunner = null)
        {
            var project = new Project(Guid.NewGuid(), "Test", "C:/Projects/Test");
            var library = project.Library;

            var donorFemaleHeavy = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "donor-f.7z", "C:/Src/df", DateTime.UtcNow, "hf",
                DonorAssetKind.FullReplacer,
                new[] { new DonorProvidedSet("dfset", "D1 Alpha", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[]
                {
                    new Piece("DFPiece", 0x11, "32 Body", "DFArma", "donor/3ba/df.nif"),
                    new Piece("DFPieceHands", 0x12, "33 Hands", "DFArmaHands", "donor/3ba/df_hands.nif"),
                }) }) },
                detectedBodySlideFiles: new[] { "body/3ba/df_slide.nif" },
                detectedPhysicsFiles: new[] { "physics/df_hdt.xml" });

            var donorMaleHeavy = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "donor-m.7z", "C:/Src/dm", DateTime.UtcNow, "hm",
                DonorAssetKind.FullReplacer,
                new[] { new DonorProvidedSet("dmset", "D2 Male", new[] { new Variant(Gender.Male, WeightClass.Heavy, new[] { new Piece("DMPiece", 0x12, "32 Body", "DMArma", "donor/himbo_hm.nif") }) }) });

            var donorBodyOnly = new DonorAsset(
                Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "donor-body.7z", "C:/Src/db", DateTime.UtcNow, "hb",
                DonorAssetKind.FullReplacer,
                new[] { new DonorProvidedSet("dbset", "D3 Body Only", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[]
                {
                    new Piece("DBPiece", 0x13, "32 Body", "DBArma", "donor/plain.nif"),
                    new Piece("DBPieceHands", 0x14, "33 Hands", "DBArmaHands", "donor/plain_hands.nif"),
                }) }) });

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
            }

            var ironArmor = new ArmorSet("IronArmor", "Iron Armor",
                new[] { new Variant(Gender.Female, WeightClass.Heavy, ironFemalePieces ?? new[]
                {
                    new Piece("IronCuirassF", 0x21, "32 Body", "IronCuirassFArma", "armor/IronCuirassF.nif"),
                }) });

            var sets = new List<ArmorSet> { ironArmor };
            if (includeMaleSet)
            {
                sets.Add(new ArmorSet("SteelArmor", "Steel Armor",
                    new[] { new Variant(Gender.Male, WeightClass.Heavy, new[]
                    {
                        new Piece("SteelCuirassM", 0x31, "32 Body", "SteelCuirassMArma", "armor/SteelCuirassM.nif"),
                    }) }));
            }

            var catalog = new Catalog(new VanillaCatalogSource("C:/Game"), sets);

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
            var vm = new OverhaulViewModel(session, selection, new MappingService(library), navigation, new ScriptedDialogService(), importRunner);
            vm.Refresh();

            return new Fixture
            {
                Vm = vm,
                Store = store,
                Overhaul = overhaul,
                Project = project,
                Library = library,
                Navigation = navigation,
                DonorFemaleHeavy = donorFemaleHeavy,
                DonorMaleHeavy = donorMaleHeavy,
                DonorBodyOnly = donorBodyOnly,
                BodyPatch = bodyPatch,
                PhysicsPatch = physicsPatch,
            };
        }
    }
}