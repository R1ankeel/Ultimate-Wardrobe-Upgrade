using FluentAssertions;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorClassifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Cls_{Guid.NewGuid():N}");
    private readonly DonorClassifier _classifier = new();

    public DonorClassifierTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string GuidFolder()
    {
        var id = Guid.NewGuid();
        var dir = Path.Combine(_root, id.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task EmptyFolder_Returns_Unknown_With_EmptySets_And_Manifest()
    {
        var dir = GuidFolder();

        var donor = await _classifier.ClassifyAsync(dir);

        donor.Kind.Should().Be(DonorAssetKind.Unknown);
        donor.ProvidedSets.Should().BeEmpty();
        donor.FileManifest.Should().BeEmpty();
        donor.DetectedBodySlideFiles.Should().BeEmpty();
        donor.DetectedPhysicsFiles.Should().BeEmpty();
        donor.ImportId.Should().Be(Guid.Parse(Path.GetFileName(dir)));
        donor.ExtractedPath.Should().Be(dir);
    }

    [Fact]
    public async Task MissingFolder_Throws()
    {
        var missing = Path.Combine(_root, "absent");
        var act = () => _classifier.ClassifyAsync(missing);
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task LooseFiles_NoPlugins_Returns_Unknown_With_Manifest_Sizes()
    {
        var dir = GuidFolder();
        WriteFile(Path.Combine(dir, "meshes", "armor", "iron", "cuirass.nif"), "nif");
        WriteFile(Path.Combine(dir, "textures", "armor", "iron", "d.dds"), "dds");
        WriteFile(Path.Combine(dir, "_meta.json"), "{}");

        var donor = await _classifier.ClassifyAsync(dir);

        donor.Kind.Should().Be(DonorAssetKind.Unknown);
        donor.ProvidedSets.Should().BeEmpty();
        donor.FileManifest.Should().HaveCount(2);
        donor.FileManifest.Should().NotContain(e => e.RelativePath == "_meta.json");
        donor.FileManifest.Should().OnlyContain(e => e.Length > 0);
        donor.FileManifest.Should().Contain(e => e.RelativePath == "meshes/armor/iron/cuirass.nif");
        donor.FileManifest.OrderBy(e => e.RelativePath).Should().BeInAscendingOrder(e => e.RelativePath);
    }

    [Fact]
    public async Task PluginFolder_Branch1_Skeleton_Is_Unknown()
    {
        var dir = GuidFolder();
        SyntheticSkyrimMods.WriteMain(dir);

        var donor = await _classifier.ClassifyAsync(dir);

        donor.Kind.Should().Be(DonorAssetKind.Unknown);
        donor.ProvidedSets.Should().BeEmpty();
        donor.FileManifest.Should().Contain(e => e.RelativePath == SyntheticSkyrimMods.MainFileName);
    }

    [Fact]
    public async Task NonGuid_FolderName_Falls_Back_To_Fresh_Guid()
    {
        var dir = Path.Combine(_root, "my-improved-armor");
        Directory.CreateDirectory(dir);

        var donor = await _classifier.ClassifyAsync(dir);

        donor.ImportId.Should().NotBe(Guid.Empty);
        donor.OriginalFileName.Should().Be("my-improved-armor");
    }

    [Fact]
    public async Task Classification_Is_Deterministic_For_Same_Folder()
    {
        var dir = Path.Combine(_root, "3030e6f0-2d4f-4a40-9e0e-615f217d3c0f");
        Directory.CreateDirectory(dir);
        WriteFile(Path.Combine(dir, "meshes", "a.nif"), "nif-body");

        var first = await _classifier.ClassifyAsync(dir);
        var second = await _classifier.ClassifyAsync(dir);

        first.ImportId.Should().Be(second.ImportId);
        first.OriginalFileName.Should().Be(second.OriginalFileName);
        first.FileManifest.Should().Equal(second.FileManifest);
    }
}