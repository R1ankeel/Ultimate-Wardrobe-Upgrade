using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Builds <see cref="RecordIndex"/> / grouping results from the Sprint 1.3
/// <see cref="SyntheticGroupingUniverse"/> mini-plugin.
/// </summary>
internal static class GroupingTestHarness
{
    public static RecordIndex BuildIndex(TestTempDir dir, out List<ScanWarning> warnings)
    {
        SyntheticGroupingUniverse.Write(dir.Root);
        warnings = new List<ScanWarning>();

        var loader = new ModLoader();
        var mods = new List<LoadedMod>();
        var loaded = loader.TryLoad(dir.File(SyntheticGroupingUniverse.FileName), warnings);
        if (loaded is not null)
        {
            mods.Add(loaded);
        }

        try
        {
            return RecordIndex.Build(mods, warnings);
        }
        finally
        {
            foreach (var m in mods)
            {
                m.Dispose();
            }
        }
    }

    public static GroupingResult Group(TestTempDir dir, out List<ScanWarning> warnings)
    {
        var index = BuildIndex(dir, out warnings);
        var correlated = new ArmorCorrelator().Correlate(index, warnings);
        return new ArmorSetGrouper().Group(correlated, index, warnings);
    }

    /// <summary>
    /// Runs the full Sprint 1.3 + 1.4 pipeline: index -> correlation -> grouping -> variant
    /// assembly, returning the assembled <see cref="ArmorSet"/>s together with the index.
    /// </summary>
    public static IReadOnlyList<UltimateWardrobe.Core.Domain.ArmorSet> Assemble(
        TestTempDir dir,
        out List<ScanWarning> warnings,
        out RecordIndex index)
    {
        index = BuildIndex(dir, out warnings);
        var correlated = new ArmorCorrelator().Correlate(index, warnings);
        var grouping = new ArmorSetGrouper().Group(correlated, index, warnings);
        return VariantAssembler.Assemble(grouping, index, warnings);
    }

    public static CorrelatedArmor CorrelateOne(TestTempDir dir, FormKey armorKey, out List<ScanWarning> warnings)
    {
        var index = BuildIndex(dir, out warnings);
        Assert.True(index.TryResolveArmor(armorKey, out var armor));
        return new ArmorCorrelator().CorrelateOne(armor, index, warnings);
    }
}