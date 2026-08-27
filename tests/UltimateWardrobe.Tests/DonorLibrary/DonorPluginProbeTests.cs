using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Tests.Scanner;

namespace UltimateWardrobe.Tests.DonorLibrary;

[Trait("Category", "Unit")]
public class DonorPluginProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"UW_Donor_Probe_{Guid.NewGuid():N}");

    public DonorPluginProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static void WriteEsm(string directory, string fileName)
    {
        var key = ModKey.FromFileName(fileName);
        var mod = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
        mod.WriteToBinary(Path.Combine(directory, fileName));
    }

    private DonorPluginProbeResult Probe(string dir, out List<ScanWarning> warnings)
    {
        warnings = new List<ScanWarning>();
        return new DonorPluginProbe().Probe(dir, warnings);
    }

    [Fact]
    public void MissingFolder_Throws()
    {
        var act = () => Probe(Path.Combine(_root, "absent"), out _);
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void NoPlugins_Returns_EmptyProbe()
    {
        WriteFile(Path.Combine(_root, "readme.txt"));

        var result = Probe(_root, out var warnings);

        warnings.Should().BeEmpty();
        result.Candidates.Should().BeEmpty();
        result.Main.Should().BeNull();
        result.MainMasters.Should().BeEmpty();
    }

    [Fact]
    public void SingleEsp_Becomes_Main()
    {
        SyntheticSkyrimMods.WriteMain(_root);

        var result = Probe(_root, out _);

        result.Candidates.Should().HaveCount(1);
        result.Main.Should().NotBeNull();
        result.Main!.ModKey.Should().Be(SyntheticSkyrimMods.MainKey);
        result.Main.IsMainPlugin.Should().BeTrue();
        result.MainMasters.Should().Contain(SyntheticSkyrimMods.MasterKey);
        result.DataPath.Should().Be(_root);
    }

    [Fact]
    public void Esp_Plus_HelperMaster_Picks_The_Unreferenced()
    {
        SyntheticSkyrimMods.WriteMaster(_root);
        SyntheticSkyrimMods.WriteMain(_root);

        var result = Probe(_root, out _);

        result.Candidates.Should().HaveCount(2);
        result.Main!.ModKey.Should().Be(SyntheticSkyrimMods.MainKey);
    }

    [Fact]
    public void Esp_Preferred_Over_Esm()
    {
        WriteEsm(_root, "X.esm");
        SyntheticSkyrimMods.WriteMain(_root);

        var result = Probe(_root, out _);

        result.Main!.ModKey.Should().Be(SyntheticSkyrimMods.MainKey);
    }

    [Fact]
    public void DataSubfolder_Layout_Is_Found()
    {
        var data = Path.Combine(_root, "Data");
        Directory.CreateDirectory(data);
        SyntheticSkyrimMods.WriteMain(data);

        var result = Probe(_root, out _);

        result.Candidates.Should().HaveCount(1);
        result.Main!.ModKey.Should().Be(SyntheticSkyrimMods.MainKey);
        result.DataPath.Should().Be(data);
    }

    [Fact]
    public void MasterChain_Picks_The_Top()
    {
        SyntheticSkyrimMods.WriteChainPlugin(_root, "A.esp");
        SyntheticSkyrimMods.WriteChainPlugin(_root, "B.esp", "A.esp");
        SyntheticSkyrimMods.WriteChainPlugin(_root, "C.esp", "B.esp");

        var result = Probe(_root, out _);

        result.Main!.ModKey.Name.Should().Be("C");
    }

    [Fact]
    public void Unreferenced_Candidates_Sorted_Ordinally()
    {
        SyntheticSkyrimMods.WriteChainPlugin(_root, "B.esp");
        SyntheticSkyrimMods.WriteChainPlugin(_root, "A.esp");
        SyntheticSkyrimMods.WriteChainPlugin(_root, "C.esp");

        var result = Probe(_root, out _);

        result.Candidates.Should().HaveCount(3);
        result.Main!.ModKey.Name.Should().Be("A");
    }

    [Fact]
    public void Corrupt_Plugin_Warns_And_Stays_A_Candidate()
    {
        SyntheticSkyrimMods.WriteCorruptPlugin(_root, "Broken.esp");

        var result = Probe(_root, out var warnings);

        warnings.Should().Contain(w => w.Message.Contains("Broken"));
        result.Candidates.Should().HaveCount(1);
        result.Main.Should().NotBeNull();
        result.MainMasters.Should().BeEmpty();
    }

    [Fact]
    public void Probe_Is_Deterministic()
    {
        SyntheticSkyrimMods.WriteMaster(_root);
        SyntheticSkyrimMods.WriteMain(_root);

        var first = Probe(_root, out _);
        var second = Probe(_root, out _);

        first.Main!.ModKey.Should().Be(second.Main!.ModKey);
    }
}