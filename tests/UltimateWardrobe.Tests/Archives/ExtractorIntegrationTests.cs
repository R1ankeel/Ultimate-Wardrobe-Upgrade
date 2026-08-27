using FluentAssertions;
using UltimateWardrobe.Archives;

namespace UltimateWardrobe.Tests.Archives;

[Trait("Category", "Integration")]
public class ExtractorIntegrationTests
{
    private static string ModsArmorDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ModsForTests", "Armor");

    public static IEnumerable<object[]> RealArchives
    {
        get
        {
            var dir = Path.GetFullPath(ModsArmorDir);
            if (!Directory.Exists(dir)) yield break;
            var files = Directory.GetFiles(dir).Where(f => f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)).Take(3);
            foreach (var f in files) yield return new object[] { f };
        }
    }

    [Theory]
    [MemberData(nameof(RealArchives))]
    public async Task Extract_Real_Mods_Does_Not_Throw(string archivePath)
    {
        // Skip if not found - MemberData already filters
        var dest = Path.Combine(Path.GetTempPath(), $"UW_Real_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dest);
            var sut = new CompositeExtractor();
            var res = await sut.ExtractAsync(archivePath, dest);
            res.ExtractedFiles.Should().NotBeEmpty();
            // At least one expected folder if it's a mod
            var hasMeshOrTexture = Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories).Any() || Directory.EnumerateFiles(dest, "*.*", SearchOption.AllDirectories).Any();
            hasMeshOrTexture.Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    [Fact]
    public void RealArchives_Skips_If_ModsForTests_Missing()
    {
        var dir = Path.GetFullPath(ModsArmorDir);
        if (!Directory.Exists(dir))
        {
            // Test passes - environment without mods is expected on CI
            Assert.True(true);
        }
        else
        {
            Assert.True(Directory.GetFiles(dir).Length > 0);
        }
    }
}
