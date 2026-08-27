using System.Diagnostics;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.Scanner;

[Trait("Category", "Integration")]
public class MutagenSpikeTests
{
    private const string SkyrimEsm = @"D:\Skymod\Stock Game\Data\Skyrim.esm";

    private readonly ITestOutputHelper _output;

    public MutagenSpikeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "Spike - overlay read of real Skyrim.esm counts ARMO, resolves ARMA/KEYW links")]
    public void Overlay_Reads_Real_SkyrimEsm_And_Resolves_Links()
    {
        if (!File.Exists(SkyrimEsm))
        {
            _output.WriteLine($"Skipped: {SkyrimEsm} is not present (integration data missing).");
            return;
        }

        var total = Stopwatch.StartNew();
        using var mod = SkyrimMod.CreateFromBinaryOverlay(SkyrimEsm, SkyrimRelease.SkyrimSE);
        var overlayMs = total.ElapsedMilliseconds;
        _output.WriteLine($"CreateFromBinaryOverlay(SkyrimSE) header parse: {overlayMs} ms");

        var armors = mod.Armors;
        var armaGroups = mod.ArmorAddons;

        var countSw = Stopwatch.StartNew();
        var armoCount = armors.RecordCache.Count;
        var countMs = countSw.ElapsedMilliseconds;
        _output.WriteLine($"ARMO group count via RecordCache: {armoCount} records ({countMs} ms)");
        Assert.True(armoCount > 100, $"Expected > 100 ARMO records from real Skyrim.esm, got {armoCount}.");

        int withArmature = 0;
        int resolvedArma = 0;
        int resolvableArmorKeyword = 0;

        IArmorGetter? sample = null;
        foreach (var armor in armors)
        {
            if (armor.Armature?.Count > 0)
            {
                withArmature++;
                if (sample is null) sample = armor;
            }
            if (armor.Keywords?.Any(k => !k.IsNull) == true) resolvableArmorKeyword++;
        }

        Assert.NotNull(sample);
        Assert.True(withArmature > 100, $"Expected > 100 ARMO with an Armature, got {withArmature}.");

        _output.WriteLine($"Sample ARMO: EditorID={sample!.EditorID} FormKey={sample.FormKey} Name={sample.Name}");
        _output.WriteLine($"  Keywords={string.Join(", ", sample.Keywords!.Select(k => k.FormKey))}");
        _output.WriteLine($"  BodyTemplate.FirstPersonFlags={sample.BodyTemplate?.FirstPersonFlags} ArmorType={sample.BodyTemplate?.ArmorType}");

        var armaDict = armaGroups.RecordCache;
        foreach (var link in sample.Armature!)
        {
            if (link.IsNull || link.FormKey.IsNull) continue;
            var arma = armaDict.TryGetValue(link.FormKey);
            if (arma is null) continue;
            resolvedArma++;

            var maleFile = arma.WorldModel?.Male?.File;
            var femaleFile = arma.WorldModel?.Female?.File;
            _output.WriteLine($"  Resolved ARMA: EditorID={arma.EditorID} FormKey={arma.FormKey}");
            _output.WriteLine($"    Male   world model: {(!(maleFile?.IsNull ?? true) ? maleFile!.GivenPath : "none")}");
            _output.WriteLine($"    Female world model: {(!(femaleFile?.IsNull ?? true) ? femaleFile!.GivenPath : "none")}");
            _output.WriteLine($"    WeightSliderEnabled: Male={arma.WeightSliderEnabled?.Male} Female={arma.WeightSliderEnabled?.Female}");
            _output.WriteLine($"    SkinTexture links:   Male={arma.SkinTexture?.Male?.FormKey} Female={arma.SkinTexture?.Female?.FormKey}");
        }

        Assert.True(resolvedArma > 0, "Expected at least one ARMA link on the sample ARMO to resolve.");

        int maleOnly = 0, femaleOnly = 0, both = 0, none = 0;
        int wsMale = 0, wsFemale = 0, wsBoth = 0;
        foreach (var kv in armaDict)
        {
            var arma = kv.Value;
            var maleFile = arma.WorldModel?.Male?.File;
            var femaleFile = arma.WorldModel?.Female?.File;
            bool hasMale = !(maleFile?.IsNull ?? true);
            bool hasFemale = !(femaleFile?.IsNull ?? true);
            if (hasMale && hasFemale) both++;
            else if (hasMale) maleOnly++;
            else if (hasFemale) femaleOnly++;
            else none++;
            bool m = arma.WeightSliderEnabled?.Male == true;
            bool f = arma.WeightSliderEnabled?.Female == true;
            if (m && f) wsBoth++;
            else if (m) wsMale++;
            else if (f) wsFemale++;
        }

        _output.WriteLine($"ARMA world-model presence: maleOnly={maleOnly} femaleOnly={femaleOnly} both={both} none={none}");
        _output.WriteLine($"ARMA WeightSliderEnabled:  male={wsMale} female={wsFemale} both={wsBoth}");
        _output.WriteLine($"ARMO with Keywords: {resolvableArmorKeyword}");

        var firstPassMs = total.ElapsedMilliseconds;
        _output.WriteLine($"First pass total: {firstPassMs} ms (count + sample link resolution + full ARMA scan)");
        _output.WriteLine($"FormKey shape: {sample.FormKey} - compare with fallback string: {sample.FormKey.IDString()}");

        Assert.True(firstPassMs < 60_000, $"First pass took {firstPassMs} ms, expected well under 60 s.");
    }
}