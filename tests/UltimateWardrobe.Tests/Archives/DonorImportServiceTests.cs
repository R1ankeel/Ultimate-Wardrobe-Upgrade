using System.Text.Json;
using FluentAssertions;
using UltimateWardrobe.Archives;

namespace UltimateWardrobe.Tests.Archives;

[Trait("Category", "Unit")]
public class DonorImportServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"UW_Import_{Guid.NewGuid():N}");

    public DonorImportServiceTests() => Directory.CreateDirectory(_tempRoot);
    public void Dispose() { try { Directory.Delete(_tempRoot, true); } catch { } }

    [Fact]
    public async Task Import_Creates_Source_And_Meta()
    {
        var zipPath = ArchiveTestHelper.CreateZipWithFiles(_tempRoot, new Dictionary<string, string>
        {
            ["meshes/armor/iron/cuirass.nif"] = "nif",
            ["textures/armor/iron/d.dds"] = "dds"
        });
        var projectRoot = Path.Combine(_tempRoot, "proj");
        Directory.CreateDirectory(projectRoot);

        var svc = new DonorImportService();
        var donor = await svc.ImportAsync(zipPath, projectRoot);

        donor.ImportId.Should().NotBe(Guid.Empty);
        donor.OriginalFileName.Should().Be(Path.GetFileName(zipPath));
        donor.ArchiveHash.Should().NotBeNullOrWhiteSpace();
        donor.ExtractedPath.Should().Be(Path.Combine(projectRoot, "Source", donor.ImportId.ToString()));
        Directory.Exists(donor.ExtractedPath).Should().BeTrue();
        File.Exists(Path.Combine(donor.ExtractedPath, "meshes", "armor", "iron", "cuirass.nif")).Should().BeTrue();
        donor.FileManifest.Should().Contain(e => e.RelativePath == "meshes/armor/iron/cuirass.nif");
        donor.FileManifest.Should().OnlyContain(e => e.Length >= 0);
        var entry = donor.FileManifest.Single(e => e.RelativePath == "textures/armor/iron/d.dds");
        entry.Length.Should().Be(File.ReadAllBytes(Path.Combine(donor.ExtractedPath, "textures", "armor", "iron", "d.dds")).Length);

        var metaPath = Path.Combine(donor.ExtractedPath, "_meta.json");
        File.Exists(metaPath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(metaPath);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("importId").GetString().Should().Be(donor.ImportId.ToString());
        doc.RootElement.GetProperty("originalFileName").GetString().Should().Be(Path.GetFileName(zipPath));
        doc.RootElement.GetProperty("archiveHash").GetString().Should().Be(donor.ArchiveHash);
        doc.RootElement.GetProperty("archiveFormat").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("extractedFilesCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Import_Hash_Stable()
    {
        var zipPath = ArchiveTestHelper.CreateZipWithFiles(_tempRoot, new Dictionary<string, string> { ["a.txt"] = "content" });
        var proj1 = Path.Combine(_tempRoot, "p1");
        var proj2 = Path.Combine(_tempRoot, "p2");
        Directory.CreateDirectory(proj1);
        Directory.CreateDirectory(proj2);
        var svc = new DonorImportService();
        var d1 = await svc.ImportAsync(zipPath, proj1);
        var d2 = await svc.ImportAsync(zipPath, proj2);
        d1.ArchiveHash.Should().Be(d2.ArchiveHash);
    }

    [Fact]
    public async Task Import_Cleans_Up_On_Failure()
    {
        var badPath = Path.Combine(_tempRoot, "bad.bin");
        await File.WriteAllBytesAsync(badPath, new byte[] { 0x00, 0x01 });
        var proj = Path.Combine(_tempRoot, "projFail");
        Directory.CreateDirectory(proj);
        var svc = new DonorImportService();
        var act = async () => await svc.ImportAsync(badPath, proj);
        await act.Should().ThrowAsync<UnsupportedArchiveException>();
        // Source folder should not remain with partial content (except maybe empty)
        var sourceDir = Path.Combine(proj, "Source");
        if (Directory.Exists(sourceDir))
        {
            Directory.GetDirectories(sourceDir).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Import_Throws_If_Archive_Missing()
    {
        var svc = new DonorImportService();
        var act = async () => await svc.ImportAsync(Path.Combine(_tempRoot, "missing.zip"), _tempRoot);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
