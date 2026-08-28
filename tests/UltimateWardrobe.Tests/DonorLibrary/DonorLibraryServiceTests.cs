using System.IO.Compression;
using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorLibraryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Svc_{Guid.NewGuid():N}");

    public DonorLibraryServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string ProjectRoot(string name = "proj") => Path.Combine(_root, name);

    private static UltimateWardrobe.Core.Domain.DonorLibrary NewLibrary() => new Project(Guid.NewGuid(), "p", "C:/unused").Library;

    private string WriteZipWith(string name, Dictionary<string, byte[]> entries)
    {
        var zipPath = Path.Combine(_root, $"{name}.zip");
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var kv in entries)
        {
            var entry = zip.CreateEntry(kv.Key, CompressionLevel.Fastest);
            using var w = entry.Open();
            w.Write(kv.Value, 0, kv.Value.Length);
        }

        return zipPath;
    }

    [Fact]
    public async Task Import_Extracts_Classifies_And_Appends_To_Library()
    {
        var projectRoot = ProjectRoot("a");
        Directory.CreateDirectory(projectRoot);
        var library = NewLibrary();
        var zipPath = WriteZipWith("donor", new Dictionary<string, byte[]>
        {
            ["meshes/armor/iron/cuirass.nif"] = "nif"u8.ToArray(),
            ["textures/armor/iron/d.dds"] = "dds"u8.ToArray()
        });

        var asset = await new DonorLibraryService().ImportAsync(zipPath, projectRoot, library);

        asset.ImportId.Should().NotBe(Guid.Empty);
        asset.OriginalFileName.Should().Be(Path.GetFileName(zipPath));
        asset.ArchiveHash.Should().NotBe("classification-pending");
        asset.ExtractedPath.Should().Be(Path.Combine(projectRoot, "Source", asset.ImportId.ToString()));

        Directory.Exists(asset.ExtractedPath).Should().BeTrue();
        File.Exists(Path.Combine(asset.ExtractedPath, "meshes", "armor", "iron", "cuirass.nif")).Should().BeTrue();
        File.Exists(Path.Combine(asset.ExtractedPath, "_meta.json")).Should().BeTrue();

        asset.ProvidedSets.Should().ContainSingle().Which.Id.Should().Be("iron");
        asset.Kind.Should().Be(DonorAssetKind.FullReplacer);
        asset.FileManifest.Should().Contain(e => e.RelativePath == "meshes/armor/iron/cuirass.nif");

        library.Assets.Should().ContainSingle().Which.ImportId.Should().Be(asset.ImportId);
    }

    [Fact]
    public async Task Reclassify_Switches_Kind_When_Reference_Carrying_Hint_Appears()
    {
        var projectRoot = ProjectRoot("b");
        Directory.CreateDirectory(projectRoot);
        var library = NewLibrary();

        // Reference-dependent donor: keyword lives in RefBase.esm which is NOT in the archive.
        var donorDir = Path.Combine(_root, "donor_build");
        DonorModBuilder.WriteReferenceDependent(donorDir);
        var espBytes = File.ReadAllBytes(Path.Combine(donorDir, DonorModBuilder.ReferenceDependentFileName));

        var zipPath = WriteZipWith("refdep", new Dictionary<string, byte[]>
        {
            [DonorModBuilder.ReferenceDependentFileName] = espBytes
        });

        var service = new DonorLibraryService();
        var imported = await service.ImportAsync(zipPath, projectRoot, library);

        // No hint -> the reference keyword cannot resolve -> falls through, no meshes -> Unknown.
        imported.Kind.Should().Be(DonorAssetKind.Unknown);
        imported.ProvidedSets.Should().BeEmpty();

        // A reference root (built-in reference-carrying hint) appears later.
        var gameRoot = Path.Combine(_root, "game");
        DonorModBuilder.WriteReferenceBase(Path.Combine(gameRoot, "Data"));
        var hint = new Catalog(new VanillaCatalogSource(gameRoot), Array.Empty<ArmorSet>());

        var reclassified = await service.ReclassifyAsync(library, imported.ImportId, hint);

        reclassified.Kind.Should().Be(DonorAssetKind.FullReplacer);
        reclassified.ProvidedSets.Should().ContainSingle().Which.Id.Should().Be("donorrp");

        // Identity fields preserved.
        reclassified.ImportId.Should().Be(imported.ImportId);
        reclassified.OriginalFileName.Should().Be(imported.OriginalFileName);
        reclassified.ArchiveHash.Should().Be(imported.ArchiveHash);
        reclassified.ExtractedPath.Should().Be(imported.ExtractedPath);

        library.Assets.Should().ContainSingle().Which.ImportId.Should().Be(imported.ImportId);
    }

    [Fact]
    public async Task Remove_Deletes_Files_And_List_Entry()
    {
        var projectRoot = ProjectRoot("c");
        Directory.CreateDirectory(projectRoot);
        var library = NewLibrary();
        var zipPath = WriteZipWith("donor", new Dictionary<string, byte[]> { ["a.nif"] = "nif"u8.ToArray() });

        var service = new DonorLibraryService();
        var asset = await service.ImportAsync(zipPath, projectRoot, library);
        var extractedPath = asset.ExtractedPath;

        Directory.Exists(extractedPath).Should().BeTrue();

        service.RemoveAsync(library, asset.ImportId);

        library.Assets.Should().BeEmpty();
        Directory.Exists(extractedPath).Should().BeFalse();
    }

    [Fact]
    public async Task Remove_Tolerates_Missing_Folder()
    {
        var projectRoot = ProjectRoot("d");
        Directory.CreateDirectory(projectRoot);
        var library = NewLibrary();
        var zipPath = WriteZipWith("donor", new Dictionary<string, byte[]> { ["a.nif"] = "nif"u8.ToArray() });

        var service = new DonorLibraryService();
        var asset = await service.ImportAsync(zipPath, projectRoot, library);
        Directory.Delete(asset.ExtractedPath, true);

        service.RemoveAsync(library, asset.ImportId);

        library.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_Library_Guard_Rejects_Owned_Archive()
    {
        var projectRootA = ProjectRoot("ga");
        var projectRootB = ProjectRoot("gb");
        Directory.CreateDirectory(projectRootA);
        Directory.CreateDirectory(projectRootB);

        var libraryA = NewLibrary();
        var libraryB = NewLibrary();

        var zipPath = WriteZipWith("shared", new Dictionary<string, byte[]> { ["a.nif"] = "nif"u8.ToArray() });

        var service = new DonorLibraryService();
        await service.ImportAsync(zipPath, projectRootA, libraryA);

        var act = async () => await service.ImportAsync(zipPath, projectRootB, libraryB, otherLibraries: new[] { libraryA });

        var ex = await act.Should().ThrowAsync<DonorAlreadyOwnedException>();
        ex.Which.OwnerProjectId.Should().Be(libraryA.ProjectId);
    }

    [Fact]
    public async Task Duplicate_Within_Same_Library_Is_Rejected()
    {
        var projectRoot = ProjectRoot("gs");
        Directory.CreateDirectory(projectRoot);
        var library = NewLibrary();
        var zipPath = WriteZipWith("dup", new Dictionary<string, byte[]> { ["a.nif"] = "nif"u8.ToArray() });

        var service = new DonorLibraryService();
        await service.ImportAsync(zipPath, projectRoot, library);

        var act = async () => await service.ImportAsync(zipPath, projectRoot, library);

        await act.Should().ThrowAsync<DonorAlreadyOwnedException>();
    }

    [Fact]
    public async Task Failed_Classification_Cleans_Up_Extracted_Folder()
    {
        var projectRoot = ProjectRoot("f");
        Directory.CreateDirectory(projectRoot);
        var library = NewLibrary();
        var zipPath = WriteZipWith("donor", new Dictionary<string, byte[]> { ["a.nif"] = "nif"u8.ToArray() });

        // A classifier that always fails lets us exercise the service-level cleanup of the
        // already-extracted Source/<ImportId>/ folder.
        var throwingClassifier = new ThrowingClassifier();

        var act = async () => await new DonorLibraryService(classifier: throwingClassifier).ImportAsync(zipPath, projectRoot, library);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // No orphan Source/<ImportId>/ subfolder remains after the failed import.
        var sourceDir = Path.Combine(projectRoot, "Source");
        if (Directory.Exists(sourceDir))
        {
            Directory.GetDirectories(sourceDir).Should().BeEmpty();
        }

        library.Assets.Should().BeEmpty();
    }

    private sealed class ThrowingClassifier : UltimateWardrobe.Core.Abstractions.IDonorClassifier
    {
        public Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog? catalogHint = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
