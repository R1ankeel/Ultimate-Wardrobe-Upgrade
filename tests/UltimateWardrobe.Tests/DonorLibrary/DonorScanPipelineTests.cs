using FluentAssertions;
using Microsoft.Extensions.Logging;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorScanPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Pipe_{Guid.NewGuid():N}");

    public DonorScanPipelineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string DonorFolder()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string GameRootCategory() => Path.Combine(_root, "Game");

    private static Catalog Hint(string gameRoot) => new(new VanillaCatalogSource(gameRoot), Array.Empty<ArmorSet>());

    private static DonorProvidedSet SingleSet(DonorAsset donor) => donor.ProvidedSets.Should().ContainSingle().Subject;

    [Fact]
    public async Task SelfContained_Plugin_Classifies_Into_Expected_Set()
    {
        var dir = DonorFolder();
        DonorModBuilder.WriteSelfContained(dir);

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        donor.Kind.Should().Be(DonorAssetKind.Unknown);
        donor.FileManifest.Should().Contain(e => e.RelativePath == DonorModBuilder.SelfContainedFileName);

        var set = SingleSet(donor);
        set.Id.Should().Be("donorkit");
        set.DisplayName.Should().Be("Donor Kit");
        set.Variants.Select(v => v.Gender).Distinct().Should().BeEquivalentTo(new[] { Gender.Male, Gender.Female });
        set.Variants.Should().OnlyContain(v => v.Weight == WeightClass.Heavy);
        foreach (var variant in set.Variants)
        {
            var piece = variant.Pieces.Should().ContainSingle().Subject;
            piece.EditorId.Should().Be("DonorKitCuirass");
            piece.MeshPath.Should().Be(DonorModBuilder.KitMeshPath);
            piece.TexturePaths.Should().Contain(DonorModBuilder.KitDiffusePath);
        }
    }

    [Fact]
    public async Task Keyword_Record_Inside_Bundled_Fake_Master_Resolves()
    {
        var dir = DonorFolder();
        DonorModBuilder.WriteBundledMaster(dir);

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        donor.FileManifest.Should().Contain(e => e.RelativePath == DonorModBuilder.BundledMasterFileName);
        donor.FileManifest.Should().Contain(e => e.RelativePath == DonorModBuilder.BundledKitFileName);

        var set = SingleSet(donor);
        set.Id.Should().Be("bundledkit");
        set.Variants.Should().HaveCount(2);
        set.Variants.Should().OnlyContain(v => v.Weight == WeightClass.Heavy);
    }

    [Fact]
    public async Task Plugin_With_Zero_Armo_Falls_Through_To_Branch2()
    {
        var dir = DonorFolder();
        DonorModBuilder.WriteEmptyEsp(dir);

        var logger = new RecordingLogger<DonorClassifier>();
        var donor = await new DonorClassifier(logger: logger).ClassifyAsync(dir);

        donor.ProvidedSets.Should().BeEmpty();
        donor.Kind.Should().Be(DonorAssetKind.Unknown);
        logger.Levels.Should().Contain(LogLevel.Warning);
        logger.Messages.Should().Contain(m => m.Contains("falling back to branch 2"));
    }

    [Fact]
    public async Task Reference_Resolves_Keyword_Without_Leaking_Reference_Armors()
    {
        var gameRoot = GameRootCategory();
        DonorModBuilder.WriteReferenceBase(Path.Combine(gameRoot, "Data"));
        var dir = DonorFolder();
        DonorModBuilder.WriteReferenceDependent(dir);

        var donor = await new DonorClassifier().ClassifyAsync(dir, Hint(gameRoot));

        var set = SingleSet(donor);
        set.Id.Should().Be("donorrp");
        set.DisplayName.Should().Be("Donor Rp");
        set.Variants.Should().HaveCount(2);
        donor.ProvidedSets.Should().NotContain(s => s.Id == "refmagerobes");
    }

    [Fact]
    public async Task Reference_Dependent_Donor_Without_Hint_Falls_Through()
    {
        var dir = DonorFolder();
        DonorModBuilder.WriteReferenceDependent(dir);

        var logger = new RecordingLogger<DonorClassifier>();
        var donor = await new DonorClassifier(logger: logger).ClassifyAsync(dir);

        donor.ProvidedSets.Should().BeEmpty();
        logger.Messages.Should().Contain(m => m.Contains("falling back to branch 2"));
    }

    [Fact]
    public async Task Donor_Bundled_Copy_Wins_Over_Same_Named_Reference()
    {
        var gameRoot = GameRootCategory();
        DonorModBuilder.WriteReferenceKeyword(Path.Combine(gameRoot, "Data"), DonorModBuilder.RefBaseFileName, "ArmorLight");

        var dir = DonorFolder();
        DonorModBuilder.WriteReferenceKeyword(dir, DonorModBuilder.RefBaseFileName, "ArmorHeavy");
        DonorModBuilder.WriteReferenceDependent(dir);

        var donor = await new DonorClassifier().ClassifyAsync(dir, Hint(gameRoot));

        var set = SingleSet(donor);
        set.Id.Should().Be("donorrp");
        set.Variants.Should().OnlyContain(v => v.Weight == WeightClass.Heavy);
    }

    [Fact]
    public async Task Corrupt_Donor_Plugin_Warns_And_Falls_Through_Never_Aborts()
    {
        var dir = DonorFolder();
        SyntheticSkyrimMods.WriteCorruptPlugin(dir, "Broken.esp");

        var donor = await new DonorClassifier().ClassifyAsync(dir);

        donor.ProvidedSets.Should().BeEmpty();
        donor.FileManifest.Should().Contain(e => e.RelativePath == "Broken.esp");
    }

    [Fact]
    public void Corrupt_Reference_Plugin_Warns_And_Is_Skipped_Never_Aborts()
    {
        var gameRoot = GameRootCategory();
        var dataPath = Path.Combine(gameRoot, "Data");
        Directory.CreateDirectory(dataPath);
        SyntheticSkyrimMods.WriteCorruptPlugin(dataPath, "BrokenRef.esm");
        var dir = DonorFolder();
        DonorModBuilder.WriteSelfContained(dir);

        var warnings = new List<ScanWarning>();
        var probe = new DonorPluginProbe().Probe(dir, warnings);
        var donorKeys = probe.Candidates.Select(c => c.ModKey).ToHashSet();
        var referencePaths = new ReferenceMasterMerger().Merge(gameRoot, donorKeys);

        referencePaths.Should().ContainSingle();
        var result = new DonorScanPipeline().Run(probe, referencePaths, warnings);

        result.ProvidedSets.Should().ContainSingle();
        result.ReferencePluginCount.Should().Be(1);
        warnings.Should().Contain(w => w.Message.Contains("BrokenRef.esm"));
    }

    [Fact]
    public void Pipeline_Reports_Donor_And_Reference_Counts()
    {
        var dir = DonorFolder();
        DonorModBuilder.WriteSelfContained(dir);

        var warnings = new List<ScanWarning>();
        var probe = new DonorPluginProbe().Probe(dir, warnings);
        var result = new DonorScanPipeline().Run(probe, Array.Empty<string>(), warnings);

        result.ProvidedSets.Should().ContainSingle();
        result.DonorArmorCount.Should().Be(1);
        result.LoadedPluginCount.Should().Be(1);
        result.ReferencePluginCount.Should().Be(0);
    }

    private sealed class RecordingLogger<T> : ILogger<T>, IDisposable
    {
        public List<LogLevel> Levels { get; } = new();
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => this;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }
}