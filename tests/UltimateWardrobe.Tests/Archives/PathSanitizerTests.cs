using FluentAssertions;
using UltimateWardrobe.Archives;

namespace UltimateWardrobe.Tests.Archives;

[Trait("Category", "Unit")]
public class PathSanitizerTests
{
    [Theory]
    [InlineData("../evil.txt", false)]
    [InlineData("..\\evil.txt", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/absolute.txt", false)]
    [InlineData("C:\\absolute.txt", false)]
    [InlineData("a/../b.txt", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsSafeEntry_Rejects_Traversal(string input, bool expected)
    {
        PathSanitizer.IsSafeEntry(input, out var sanitized).Should().Be(expected);
    }

    [Theory]
    [InlineData("meshes/armor/iron.nif", "meshes/armor/iron.nif")]
    [InlineData("meshes\\armor\\iron.nif", "meshes/armor/iron.nif")]
    [InlineData("a/b/c.txt", "a/b/c.txt")]
    public void IsSafeEntry_Accepts_Normal(string input, string expected)
    {
        PathSanitizer.IsSafeEntry(input, out var sanitized).Should().BeTrue();
        sanitized.Should().Be(expected);
    }

    [Fact]
    public void GetSafeFullPath_Prevents_Escape()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"UW_San_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dest);
            var full = PathSanitizer.GetSafeFullPath(dest, "a/b.txt");
            full.Should().StartWith(Path.GetFullPath(dest));
        }
        finally { try { Directory.Delete(dest, true); } catch { } }
    }
}
