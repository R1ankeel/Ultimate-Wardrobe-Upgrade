using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class FileResolverTests
{
    private static string Normalize(string path) => path.Replace('\\', '/');

    private static void WriteFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void ResolveMesh_DataLayout_ReturnsNormalizedPath()
    {
        using var dir = new TestTempDir();
        var file = dir.File("Data\\meshes\\armor\\iron\\cuirass_1.nif");
        WriteFile(file);

        var resolver = new FileResolver(dir.Root);

        var result = resolver.ResolveMesh("armor/iron/cuirass_1.nif");

        Assert.NotNull(result);
        Assert.Equal(Normalize(dir.File("Data\\meshes\\armor\\iron\\cuirass_1.nif")), result);
    }

    [Fact]
    public void ResolveMesh_FolderModLayout_WithoutData_FindsFile()
    {
        using var dir = new TestTempDir();
        var file = dir.File("meshes\\armor\\iron\\cuirass_1.nif");
        WriteFile(file);

        var resolver = new FileResolver(dir.Root);

        var result = resolver.ResolveMesh("armor/iron/cuirass_1.nif");

        Assert.Equal(Normalize(file), result);
    }

    [Fact]
    public void ResolveMesh_NormalizesBackslashes_InInputAndResult()
    {
        using var dir = new TestTempDir();
        var file = dir.File("Data\\meshes\\armor\\iron\\cuirass_1.nif");
        WriteFile(file);

        var resolver = new FileResolver(dir.Root);

        var result = resolver.ResolveMesh("armor\\iron\\cuirass_1.nif");

        Assert.NotNull(result);
        Assert.DoesNotContain('\\', result);
        Assert.Equal(Normalize(file), result);
    }

    [Fact]
    public void ResolveTexture_DataLayout_ReturnsNormalizedPath()
    {
        using var dir = new TestTempDir();
        var file = dir.File("Data\\textures\\armor\\iron\\cuirass_1.dds");
        WriteFile(file);

        var resolver = new FileResolver(dir.Root);

        var result = resolver.ResolveTexture("armor/iron/cuirass_1.dds");

        Assert.Equal(Normalize(file), result);
    }

    [Fact]
    public void ResolveTexture_FolderModLayout_WithoutData_FindsFile()
    {
        using var dir = new TestTempDir();
        var file = dir.File("textures\\armor\\iron\\cuirass_1.dds");
        WriteFile(file);

        var resolver = new FileResolver(dir.Root);

        var result = resolver.ResolveTexture("armor/iron/cuirass_1.dds");

        Assert.Equal(Normalize(file), result);
    }

    [Fact]
    public void MissingFile_ReturnsNull_AndCountsMissing()
    {
        using var dir = new TestTempDir();

        var resolver = new FileResolver(dir.Root);

        var result = resolver.ResolveMesh("armor/iron/absent.nif");

        Assert.Null(result);
        Assert.Equal(1, resolver.MissingFiles);
    }

    [Fact]
    public void MissingFile_CountsOncePerCall_BSAPackedScenario()
    {
        using var dir = new TestTempDir();

        var resolver = new FileResolver(dir.Root);

        Assert.Null(resolver.ResolveMesh("armor/iron/a.nif"));
        Assert.Null(resolver.ResolveMesh("armor/iron/b.nif"));
        Assert.Equal(2, resolver.MissingFiles);
    }

    [Fact]
    public void MissingFile_DoesNotReturnWarning_OnlyCounts()
    {
        using var dir = new TestTempDir();

        var resolver = new FileResolver(dir.Root);

        Assert.Null(resolver.ResolveTexture("armor/iron/absent.dds"));
        Assert.Equal(1, resolver.MissingFiles);
        Assert.True(resolver.MissingFiles == 1);
    }

    [Fact]
    public void Resolve_RawPath_WithoutCategoryStem()
    {
        using var dir = new TestTempDir();
        var file = dir.File("Data\\some\\other\\asset.txt");
        WriteFile(file);

        var resolver = new FileResolver(dir.Root);

        var result = resolver.Resolve("some/other/asset.txt");

        Assert.Equal(Normalize(file), result);
    }
}
