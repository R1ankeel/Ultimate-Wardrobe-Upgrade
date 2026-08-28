using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Patcher;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.Patcher;

/// <summary>
/// Sprint 5.2.3 - <see cref="FileSlicer"/> / <see cref="DonorFileLocator"/> unit tests over
/// runtime-synthesized donor folders. Locks: the exact whitelisted selection set (mesh + <c>_1st</c>
/// alternates + provided textures with folder-mirror fallback + matching BodySlide + physics),
/// whole-export de-duplication, the body-then-physics patch overlay (<c>last wins</c> + patch-only
/// body/skse content + junk exclusion), physics-from-patch replacing donor physics, the
/// missing-primary-mesh skip, the traversal rejection, root-vs-<c>Data/</c> layout parity,
/// cancellation and non-mapped mapping handling.
/// </summary>
public sealed class FileSlicerTests
{
    // ---------------------------------------------------------------------------------------------
    // Exact whitelisted selection set (2.1 / 2.2)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Slice_SelectedSet_IsExactlyMeshFirstPersonTexturesBodySlidePhysics()
    {
        using var dir = new TestTempDir();
        var donor = WriteDonor(dir.Root, "donor-root",
            new[]
            {
                "meshes/armor/iron/m/cuirass.nif",
                "meshes/armor/iron/m/cuirass_1st.nif",
                "meshes/armor/iron/m/cuirass_1stperson.nif",
                "meshes/armor/iron/m/cuirass_0.nif",
                "meshes/armor/iron/m/gauntlets_0.nif",
                "textures/armor/iron/m/cuirass.dds",
                "textures/armor/iron/m/cuirass_n.dds",
                "textures/armor/iron/m/notes.txt",
                "CalienteTools/BodySlide/SliderSets/CuirassBodySlide.osp",
                "CalienteTools/BodySlide/SliderSets/Unrelated.osp",
                "SKSE/Plugins/IronArmorPhysics.xml",
                "SKSE/Plugins/Unrelated.xml",
            },
            bodySlide: new[]
            {
                "CalienteTools/BodySlide/SliderSets/CuirassBodySlide.osp",
                "CalienteTools/BodySlide/SliderSets/Unrelated.osp",
            },
            physics: new[]
            {
                "SKSE/Plugins/IronArmorPhysics.xml",
                "SKSE/Plugins/Unrelated.xml",
            });

        var mapping = MakeMapping(donor.Asset);
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(new[] { mapping.Mapping }, MakeLibrary(donor.Asset), export);

        var expected = new[]
        {
            "CalienteTools/BodySlide/SliderSets/CuirassBodySlide.osp",
            "SKSE/Plugins/IronArmorPhysics.xml",
            "meshes/armor/iron/m/cuirass.nif",
            "meshes/armor/iron/m/cuirass_1st.nif",
            "meshes/armor/iron/m/cuirass_1stperson.nif",
            "textures/armor/iron/m/cuirass.dds",
            "textures/armor/iron/m/cuirass_n.dds",
        };

        Assert.Equal(expected, OutputTree(export));
        Assert.Equal(expected, slice.CopiedFiles);
        Assert.Equal(0, slice.SkippedMappings);
        Assert.Empty(slice.Warnings);
        Assert.Equal(ExpectedBytes(donor.Directory, expected), slice.CopiedBytes);
    }

    [Fact]
    public void Slice_ProvidedSetTextures_WinOverFolderMirrorFallback()
    {
        using var dir = new TestTempDir();
        var piece = new Piece("DonorIronCuirass", 0x12E46, "32 Body", null, "meshes/armor/iron/m/cuirass.nif",
            new[] { "textures/armor/iron/m/special_a.dds", "textures/armor/iron/m/special_b.dds" });
        var providedSet = new DonorProvidedSet("DonorIron", "Donor Iron",
            new[] { new Variant(Gender.Male, WeightClass.Heavy, new[] { piece }) });

        var donor = WriteDonor(dir.Root, "donor-provided",
            new[]
            {
                "meshes/armor/iron/m/cuirass.nif",
                "textures/armor/iron/m/special_a.dds",
                "textures/armor/iron/m/special_b.dds",
                "textures/armor/iron/m/other.dds",
            },
            provided: new[] { providedSet });

        var mapping = MakeMapping(donor.Asset, pieceEditorId: "DonorIronCuirass");
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(new[] { mapping.Mapping }, MakeLibrary(donor.Asset), export);

        var expected = new[]
        {
            "meshes/armor/iron/m/cuirass.nif",
            "textures/armor/iron/m/special_a.dds",
            "textures/armor/iron/m/special_b.dds",
        };

        Assert.Equal(expected, OutputTree(export));
        Assert.Empty(slice.Warnings);
    }

    // ---------------------------------------------------------------------------------------------
    // Whole-export de-duplication
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Slice_TwoMappingsSharingTexture_CopiesItOnce()
    {
        using var dir = new TestTempDir();
        var cuirass = new Piece("DonorIronCuirass", 0x12E46, "32 Body", null, "meshes/armor/iron/m/cuirass.nif",
            new[] { "textures/shared/shared.dds" });
        var gauntlets = new Piece("DonorIronGauntlets", 0x12E47, "36 Hands", null, "meshes/armor/iron/m/gauntlets.nif",
            new[] { "textures/shared/shared.dds" });
        var providedSet = new DonorProvidedSet("DonorIron", "Donor Iron",
            new[] { new Variant(Gender.Male, WeightClass.Heavy, new[] { cuirass, gauntlets }) });

        var donor = WriteDonor(dir.Root, "donor-shared",
            new[]
            {
                "meshes/armor/iron/m/cuirass.nif",
                "meshes/armor/iron/m/gauntlets.nif",
                "textures/shared/shared.dds",
            },
            provided: new[] { providedSet });

        var cuirassMapping = MakeMapping(donor.Asset, "meshes/armor/iron/m/cuirass.nif", pieceEditorId: "DonorIronCuirass");
        var gauntletsMapping = MakeMapping(donor.Asset, "meshes/armor/iron/m/gauntlets.nif", pieceEditorId: "DonorIronGauntlets");
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(
            new[] { cuirassMapping.Mapping, gauntletsMapping.Mapping },
            MakeLibrary(donor.Asset),
            export);

        var expected = new[]
        {
            "meshes/armor/iron/m/cuirass.nif",
            "meshes/armor/iron/m/gauntlets.nif",
            "textures/shared/shared.dds",
        };

        Assert.Equal(expected, OutputTree(export));
        Assert.Equal(3, slice.CopiedFiles.Count);
        Assert.Equal(ExpectedBytes(donor.Directory, expected), slice.CopiedBytes);
    }

    // ---------------------------------------------------------------------------------------------
    // Patch overlays (amendment #8, body then physics - last wins)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Slice_PatchOverlay_BodyThenPhysics_LastWins_AndJunkExcluded()
    {
        using var dir = new TestTempDir();
        var donor = WriteDonor(dir.Root, "donor-mesh",
            new[]
            {
                "meshes/armor/iron/m/cuirass.nif",
                "CalienteTools/BodySlide/SliderSets/Shared.osp",
            },
            bodySlide: new[] { "CalienteTools/BodySlide/SliderSets/Shared.osp" });

        var body = WriteDonor(dir.Root, "body-patch",
            new[]
            {
                "meshes/armor/iron/m/cuirass.nif",
                "CalienteTools/BodySlide/SliderSets/Shared.osp",
                "CalienteTools/BodySlide/SliderSets/BodyOnly.osp",
                "SKSE/Plugins/body.xml",
                "Docs/readme.txt",
            },
            kind: DonorAssetKind.BodyConversionPatch);

        var physics = WriteDonor(dir.Root, "physics-patch",
            new[]
            {
                "meshes/armor/iron/m/cuirass.nif",
                "SKSE/Plugins/body.xml",
                "CalienteTools/BodySlide/SliderSets/PhysicsOnly.osp",
                "SKSE/Plugins/physics.xml",
                "readme.txt",
            },
            kind: DonorAssetKind.PhysicsPatch);

        var mapping = MakeMapping(donor.Asset,
            body: body.Asset.ImportId,
            physics: physics.Asset.ImportId);
        var export = Path.Combine(dir.Root, "Export");
        var library = MakeLibrary(donor.Asset, body.Asset, physics.Asset);

        var slice = new FileSlicer().Slice(new[] { mapping.Mapping }, library, export);

        var expected = new[]
        {
            "CalienteTools/BodySlide/SliderSets/BodyOnly.osp",
            "CalienteTools/BodySlide/SliderSets/PhysicsOnly.osp",
            "CalienteTools/BodySlide/SliderSets/Shared.osp",
            "SKSE/Plugins/body.xml",
            "SKSE/Plugins/physics.xml",
            "meshes/armor/iron/m/cuirass.nif",
        };

        Assert.Equal(expected, OutputTree(export));

        // The physics patch is evaluated last and shadows the body patch at every colliding path.
        Assert.Equal(Content(physics.Directory, "meshes/armor/iron/m/cuirass.nif"),
            File.ReadAllText(Path.Combine(export, "meshes", "armor", "iron", "m", "cuirass.nif")));
        Assert.Equal(Content(physics.Directory, "SKSE/Plugins/body.xml"),
            File.ReadAllText(Path.Combine(export, "SKSE", "Plugins", "body.xml")));

        // The donor's BodySlide is shadowed by the body patch's (the overlay wins on collision),
        // but the physics patch has no such file - the body version stands.
        Assert.Equal(Content(body.Directory, "CalienteTools/BodySlide/SliderSets/Shared.osp"),
            File.ReadAllText(Path.Combine(export, "CalienteTools", "BodySlide", "SliderSets", "Shared.osp")));

        Assert.Empty(slice.Warnings);
        Assert.Equal(0, slice.SkippedMappings);
    }

    [Fact]
    public void Slice_PhysicsPatchAttached_ReplacesTheDonorsOwnPhysicsFiles()
    {
        using var dir = new TestTempDir();
        var donor = WriteDonor(dir.Root, "donor-with-physics",
            new[]
            {
                "meshes/armor/iron/m/cuirass.nif",
                "SKSE/Plugins/IronArmorPhysics.xml",
            },
            physics: new[] { "SKSE/Plugins/IronArmorPhysics.xml" });

        var physicsPatch = WriteDonor(dir.Root, "physics-patch",
            new[] { "SKSE/Plugins/PatchPhysics.xml" },
            kind: DonorAssetKind.PhysicsPatch);

        var mapping = MakeMapping(donor.Asset, physics: physicsPatch.Asset.ImportId);
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(
            new[] { mapping.Mapping },
            MakeLibrary(donor.Asset, physicsPatch.Asset),
            export);

        // With a physics patch attached the patch's SKSE content is sliced and the donor's own
        // physics detection is suppressed.
        Assert.Equal(
            new[]
            {
                "SKSE/Plugins/PatchPhysics.xml",
                "meshes/armor/iron/m/cuirass.nif",
            },
            OutputTree(export));
        Assert.Empty(slice.Warnings);
    }

    // ---------------------------------------------------------------------------------------------
    // Missing primary mesh / warnings / skips
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Slice_MissingPrimaryMesh_SkipsMappingWithWarning_AndCopiesNothingForIt()
    {
        using var dir = new TestTempDir();
        var donor = WriteDonor(dir.Root, "donor-missing",
            new[]
            {
                "meshes/armor/iron/m/gauntlets.nif",
                "textures/armor/iron/m/gauntlets.dds",
            });

        var bad = MakeMapping(donor.Asset, "meshes/armor/iron/m/cuirass.nif");
        var good = MakeMapping(donor.Asset, "meshes/armor/iron/m/gauntlets.nif");
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(
            new[] { bad.Mapping, good.Mapping },
            MakeLibrary(donor.Asset),
            export);

        Assert.Equal(1, slice.SkippedMappings);
        Assert.Single(slice.Warnings, w => w.Message.Contains("was not found in asset", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                "meshes/armor/iron/m/gauntlets.nif",
                "textures/armor/iron/m/gauntlets.dds",
            },
            OutputTree(export));
        Assert.DoesNotContain(OutputTree(export), p => p.Contains("cuirass", StringComparison.Ordinal));
    }

    [Fact]
    public void Slice_MissingTexture_WarnsButMappingStillCopiesItsMesh()
    {
        using var dir = new TestTempDir();
        // The provided set lists a texture that does not physically exist in the donor folder.
        var piece = new Piece("DonorIronCuirass", 0x12E46, "32 Body", null, "meshes/armor/iron/m/cuirass.nif",
            new[] { "textures/armor/iron/m/ghost.dds" });
        var providedSet = new DonorProvidedSet("DonorIron", "Donor Iron",
            new[] { new Variant(Gender.Male, WeightClass.Heavy, new[] { piece }) });

        var donor = WriteDonor(dir.Root, "donor-ghost-texture",
            new[] { "meshes/armor/iron/m/cuirass.nif" },
            provided: new[] { providedSet });

        var mapping = MakeMapping(donor.Asset, pieceEditorId: "DonorIronCuirass");
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(new[] { mapping.Mapping }, MakeLibrary(donor.Asset), export);

        Assert.Equal(new[] { "meshes/armor/iron/m/cuirass.nif" }, OutputTree(export));
        Assert.Single(slice.Warnings, w => w.Message.Contains("textures/armor/iron/m/ghost.dds", StringComparison.Ordinal));
        Assert.Equal(0, slice.SkippedMappings);
    }

    // ---------------------------------------------------------------------------------------------
    // Traversal guard
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Locator_TraversalAndInvalidPaths_AreRejected()
    {
        using var dir = new TestTempDir();
        var donorDir = Path.Combine(dir.Root, "donor");
        Directory.CreateDirectory(donorDir);
        File.WriteAllText(Path.Combine(dir.Root, "secret.nif"), "outside");

        var locator = new DonorFileLocator(donorDir);

        Assert.Null(locator.TryLocate("../secret.nif"));
        Assert.Null(locator.TryLocate("meshes/../secret.nif"));
        Assert.Null(locator.TryLocate("/"));
        Assert.Null(locator.TryLocate(""));
        Assert.Null(locator.TryLocate(null));
        Assert.Null(locator.TryLocate("meshes/armor/iron/m/missing.nif"));
    }

    [Fact]
    public void Slice_TraversalProbeMesh_SkipsMappingWithoutReadingOutside()
    {
        using var dir = new TestTempDir();
        var donorDir = Path.Combine(dir.Root, "DonorProbe");
        Directory.CreateDirectory(donorDir);
        File.WriteAllText(Path.Combine(dir.Root, "secret.nif"), "outside-content");

        var donor = new DonorAsset(
            Guid.NewGuid(), "donor-probe.7z", donorDir, DateTime.UtcNow, "hash",
            DonorAssetKind.FullReplacer,
            fileManifest: new[] { new DonorFileEntry("../secret.nif", 14) });

        var mapping = MakeMapping(donor, "meshes/../secret.nif");
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(new[] { mapping.Mapping }, MakeLibrary(donor), export);

        Assert.Equal(1, slice.SkippedMappings);
        Assert.Contains(slice.Warnings, w => w.Message.Contains("was not found in asset", StringComparison.Ordinal));
        Assert.Empty(OutputTree(export));
        Assert.False(File.Exists(Path.Combine(export, "secret.nif")));
    }

    // ---------------------------------------------------------------------------------------------
    // Root-vs-Data layout parity
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Locator_ResolvesRootAndDataLayouts_ToTheSameGameRelativePath()
    {
        using var dir = new TestTempDir();
        var rootLayout = Path.Combine(dir.Root, "RootDonor");
        var dataLayout = Path.Combine(dir.Root, "DataDonor");
        WriteAndCreate("meshes/armor/iron/cuirass.nif", rootLayout);
        WriteAndCreate("Data/meshes/armor/iron/cuirass.nif", dataLayout);

        var rootLocator = new DonorFileLocator(rootLayout);
        var dataLocator = new DonorFileLocator(dataLayout);

        var rootPhysical = rootLocator.TryLocate("meshes/armor/iron/cuirass.nif");
        var dataPhysical = dataLocator.TryLocate("meshes/armor/iron/cuirass.nif");

        Assert.NotNull(rootPhysical);
        Assert.Equal(Path.Combine(rootLayout, "meshes", "armor", "iron", "cuirass.nif"), rootPhysical);
        Assert.NotNull(dataPhysical);
        Assert.Equal(Path.Combine(dataLayout, "Data", "meshes", "armor", "iron", "cuirass.nif"), dataPhysical);
    }

    [Fact]
    public void Slice_RootAndDataLayoutDonors_ProduceIdenticalOutputTrees()
    {
        using var dir = new TestTempDir();
        var rootLayout = WriteDonor(dir.Root, "donor-root",
            new[]
            {
                "meshes/armor/iron/cuirass.nif",
                "textures/armor/iron/cuirass.dds",
            });
        var dataLayout = WriteDonor(dir.Root, "donor-data",
            new[]
            {
                "meshes/armor/iron/cuirass.nif",
                "textures/armor/iron/cuirass.dds",
            },
            dataLayout: true);

        var rootMapping = MakeMapping(rootLayout.Asset, "meshes/armor/iron/cuirass.nif");
        var dataMapping = MakeMapping(dataLayout.Asset, "meshes/armor/iron/cuirass.nif", pieceEditorId: "DonorIronCuirass");
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(
            new[] { rootMapping.Mapping, dataMapping.Mapping },
            MakeLibrary(rootLayout.Asset, dataLayout.Asset),
            export);

        Assert.Equal(
            new[]
            {
                "meshes/armor/iron/cuirass.nif",
                "textures/armor/iron/cuirass.dds",
            },
            OutputTree(export));
        Assert.Empty(slice.Warnings);
        Assert.Equal(0, slice.SkippedMappings);
    }

    // ---------------------------------------------------------------------------------------------
    // Cancellation + non-mapped mappings
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Slice_PreCancelledToken_ThrowsOperationCanceled()
    {
        using var dir = new TestTempDir();
        var donor = WriteDonor(dir.Root, "donor-cancel",
            new[] { "meshes/armor/iron/cuirass.nif" });
        var mapping = MakeMapping(donor.Asset);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new FileSlicer().Slice(
                new[] { mapping.Mapping },
                MakeLibrary(donor.Asset),
                Path.Combine(dir.Root, "Export"),
                cts.Token));
    }

    [Fact]
    public void Slice_NonMappedMapping_ProducesNothing()
    {
        using var dir = new TestTempDir();
        var donor = WriteDonor(dir.Root, "donor-unmapped",
            new[] { "meshes/armor/iron/cuirass.nif" });
        var mapping = MakeMapping(donor.Asset, status: MappingStatus.Pending);
        var export = Path.Combine(dir.Root, "Export");

        var slice = new FileSlicer().Slice(new[] { mapping.Mapping }, MakeLibrary(donor.Asset), export);

        Assert.Equal(0, slice.SkippedMappings);
        Assert.Empty(slice.Warnings);
        Assert.Empty(slice.CopiedFiles);
        Assert.Equal(0, slice.CopiedBytes);
        Assert.False(Directory.Exists(export));
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private static (string Directory, DonorAsset Asset) WriteDonor(
        string parent,
        string name,
        IReadOnlyList<string> files,
        bool dataLayout = false,
        IReadOnlyList<string>? bodySlide = null,
        IReadOnlyList<string>? physics = null,
        IReadOnlyList<DonorProvidedSet>? provided = null,
        DonorAssetKind kind = DonorAssetKind.FullReplacer)
    {
        var directory = Path.Combine(parent, name);
        Directory.CreateDirectory(directory);

        var manifest = new List<DonorFileEntry>(files.Count);
        foreach (var file in files)
        {
            var diskRelative = dataLayout ? "Data/" + file : file;
            WriteAndCreate(diskRelative, directory);
            manifest.Add(new DonorFileEntry(diskRelative, new FileInfo(Path.Combine(directory, diskRelative.Replace('/', Path.DirectorySeparatorChar))).Length));
        }

        var asset = new DonorAsset(
            Guid.NewGuid(),
            name + ".7z",
            directory,
            DateTime.UtcNow,
            "hash-" + name,
            kind,
            provided,
            manifest,
            bodySlide ?? Array.Empty<string>(),
            physics ?? Array.Empty<string>());

        return (directory, asset);
    }

    private static void WriteAndCreate(string relativePath, string root)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        // Distinct per-file content so overlay/copy-source assertions can tell files apart.
        File.WriteAllText(full, Content(root, relativePath));
    }

    private static string Content(string root, string relativePath)
    {
        return Path.GetFileName(root) + "::" + relativePath;
    }

    private static (PieceMapping Mapping, Guid DonorId) MakeMapping(
        DonorAsset donor,
        string? mesh = null,
        string pieceEditorId = "DonorIronCuirass",
        string setId = "IronArmor",
        Guid? body = null,
        Guid? physics = null,
        MappingStatus status = MappingStatus.Mapped)
    {
        var overhaulId = Guid.NewGuid();
        var mapping = new PieceMapping(
            Guid.NewGuid(),
            overhaulId,
            setId,
            "IronCuirass",
            Gender.Male,
            donor.ImportId,
            pieceEditorId,
            mesh ?? "meshes/armor/iron/m/cuirass.nif",
            body,
            physics,
            status);
        return (mapping, donor.ImportId);
    }

    private static UltimateWardrobe.Core.Domain.DonorLibrary MakeLibrary(params DonorAsset[] assets)
    {
        var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
        library.Assets.AddRange(assets);
        return library;
    }

    private static IReadOnlyList<string> OutputTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static long ExpectedBytes(string donorRoot, IReadOnlyList<string> gameRelativePaths)
    {
        return gameRelativePaths.Sum(p => File.Exists(Path.Combine(donorRoot, p.Replace('/', Path.DirectorySeparatorChar)))
            ? new FileInfo(Path.Combine(donorRoot, p.Replace('/', Path.DirectorySeparatorChar))).Length
            : 0);
    }
}