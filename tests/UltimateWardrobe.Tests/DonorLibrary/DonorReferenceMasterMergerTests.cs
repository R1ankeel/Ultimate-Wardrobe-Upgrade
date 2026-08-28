using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorReferenceMasterMergerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Merge_{Guid.NewGuid():N}");

    public DonorReferenceMasterMergerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string GameData()
    {
        var data = Path.Combine(_root, "Game", "Data");
        Directory.CreateDirectory(data);
        return data;
    }

    [Fact]
    public void Missing_Root_Returns_Empty()
    {
        var result = new ReferenceMasterMerger().Merge(Path.Combine(_root, "absent"), new HashSet<ModKey>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void Empty_Root_Path_Returns_Empty()
    {
        var result = new ReferenceMasterMerger().Merge("   ", new HashSet<ModKey>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void Enumerates_Esm_And_Esl_Only_From_Data_Layout_Ordinally()
    {
        var data = GameData();
        DonorModBuilder.WriteEmptyReference(data, "PluginsOnly.esp");
        DonorModBuilder.WriteEmptyReference(data, "Zed.esm");
        DonorModBuilder.WriteEmptyReference(data, "Alpha.esm");
        DonorModBuilder.WriteEmptyReference(data, "Beta.esl");

        var result = new ReferenceMasterMerger().Merge(Path.Combine(_root, "Game"), new HashSet<ModKey>());

        result.Should().HaveCount(3);
        result.Select(p => Path.GetExtension(p)).Should().OnlyContain(ext => ext == ".esm" || ext == ".esl");
        result.Select(p => Path.GetFileName(p)).Should().ContainInOrder("Alpha.esm", "Beta.esl", "Zed.esm");
    }

    [Fact]
    public void Root_Layout_Is_Used_When_No_Data_Folder()
    {
        DonorModBuilder.WriteEmptyReference(_root, "GameR.esm");

        var result = new ReferenceMasterMerger().Merge(_root, new HashSet<ModKey>());

        result.Should().ContainSingle();
        Path.GetFileName(result[0]).Should().Be("GameR.esm");
    }

    [Fact]
    public void Donor_Owned_Names_Are_Excluded_From_The_Reference()
    {
        var data = GameData();
        DonorModBuilder.WriteEmptyReference(data, "BundledBase.esm");
        DonorModBuilder.WriteEmptyReference(data, "Keep.esl");

        var donorKeys = new HashSet<ModKey> { DonorModBuilder.BundledMasterKey };

        var result = new ReferenceMasterMerger().Merge(Path.Combine(_root, "Game"), donorKeys);

        result.Should().ContainSingle();
        Path.GetFileName(result[0]).Should().Be("Keep.esl");
    }

    [Fact]
    public void Duplicate_Reference_Names_Keep_The_Ordinal_First()
    {
        var data = GameData();
        DonorModBuilder.WriteEmptyReference(data, "Same.esl");
        DonorModBuilder.WriteEmptyReference(data, "Same.esm");
        DonorModBuilder.WriteEmptyReference(data, "Other.esm");

        var result = new ReferenceMasterMerger().Merge(Path.Combine(_root, "Game"), new HashSet<ModKey>());

        result.Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "Other.esm", "Same.esl" }, opts => opts.WithStrictOrdering());
    }
}