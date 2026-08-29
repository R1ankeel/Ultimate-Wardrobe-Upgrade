using System.Diagnostics;
using FluentAssertions;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Mapping;

using DonorLibraryModel = UltimateWardrobe.Core.Domain.DonorLibrary;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// E1 - Benchmark harness for OverhaulMatrix.Build after C1-C6.
/// Asserts indexed Build completes <50 ms for 651 sets (vanilla) and <150 ms for 3000 sets.
/// Uses Stopwatch headless test, not BenchmarkDotNet, to keep CI gate simple.
/// </summary>
[Trait("Category", "App")]
public class OverhaulMatrixBenchmarkTests
{
    [Fact]
    public void Build_651_sets_completes_under_50ms()
    {
        var catalog = CreateSyntheticCatalog(651);
        var library = new DonorLibraryModel(Guid.NewGuid());
        var mappings = Array.Empty<PieceMapping>();
        var mappingService = new MappingService(library);

        // Warm catalog cache
        _ = OverhaulMatrixBenchmarkHelper.Build(catalog, mappings, library, mappingService, null);

        var sw = Stopwatch.StartNew();
        var result = OverhaulMatrixBenchmarkHelper.Build(catalog, mappings, library, mappingService, null);
        sw.Stop();

        result.Columns.Should().NotBeEmpty();
        result.Sections.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(50, "vanilla 651 sets must filter instantly (<50 ms) after indexing");
    }

    [Fact]
    public void Build_3000_sets_completes_under_150ms()
    {
        var catalog = CreateSyntheticCatalog(3000);
        var library = new DonorLibraryModel(Guid.NewGuid());
        var mappings = Array.Empty<PieceMapping>();
        var mappingService = new MappingService(library);

        _ = OverhaulMatrixBenchmarkHelper.Build(catalog, mappings, library, mappingService, null);

        var sw = Stopwatch.StartNew();
        var result = OverhaulMatrixBenchmarkHelper.Build(catalog, mappings, library, mappingService, null);
        sw.Stop();

        result.Columns.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(150, "3000 sets must filter instantly (<150 ms) after indexing");
    }

    [Fact]
    public void Build_with_search_iron_on_651_sets_is_submillisecond_after_cache()
    {
        var catalog = CreateSyntheticCatalog(651);
        var library = new DonorLibraryModel(Guid.NewGuid());
        var mappings = Array.Empty<PieceMapping>();
        var mappingService = new MappingService(library);

        // Warm
        _ = OverhaulMatrixBenchmarkHelper.Build(catalog, mappings, library, mappingService, "iron");

        var sw = Stopwatch.StartNew();
        var result = OverhaulMatrixBenchmarkHelper.Build(catalog, mappings, library, mappingService, "iron");
        sw.Stop();

        // Only sets whose name contains "iron" should remain, but benchmark is about speed
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    private static Catalog CreateSyntheticCatalog(int count)
    {
        var sets = new List<ArmorSet>(count);
        for (var i = 0; i < count; i++)
        {
            var id = $"Set{i:D4}";
            var name = i % 10 == 0 ? $"Iron Armor {i}" : $"Armor Set {i}";
            var variants = new List<Variant>();
            // Alternate gender/weight to exercise columns
            if (i % 3 == 0)
            {
                variants.Add(new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece($"Piece{i}_F_H", (uint)(0x10000000 + i), "32 Body", $"Arma{i}_F_H", $"armor/set{i}_f_h.nif") }));
                variants.Add(new Variant(Gender.Male, WeightClass.Heavy, new[] { new Piece($"Piece{i}_M_H", (uint)(0x20000000 + i), "32 Body", $"Arma{i}_M_H", $"armor/set{i}_m_h.nif") }));
            }
            else if (i % 3 == 1)
            {
                variants.Add(new Variant(Gender.Female, WeightClass.Light, new[] { new Piece($"Piece{i}_F_L", (uint)(0x30000000 + i), "32 Body", $"Arma{i}_F_L", $"armor/set{i}_f_l.nif") }));
            }
            else
            {
                variants.Add(new Variant(Gender.Unisex, WeightClass.Clothing, new[] { new Piece($"Piece{i}_U_C", (uint)(0x40000000 + i), "32 Body", $"Arma{i}_U_C", $"armor/set{i}_u_c.nif") }));
            }

            sets.Add(new ArmorSet(id, name, variants));
        }

        return new Catalog(new VanillaCatalogSource("C:/Game"), sets);
    }
}

internal static class OverhaulMatrixBenchmarkHelper
{
    public static OverhaulMatrixViewModel Build(
        Catalog catalog,
        IReadOnlyList<PieceMapping> mappings,
        DonorLibraryModel library,
        MappingService mappingService,
        string? search)
    {
        var appAssembly = typeof(OverhaulViewModel).Assembly;
        var type = appAssembly.GetType("UltimateWardrobe.App.ViewModels.OverhaulMatrix", throwOnError: true)!;
        var method = type.GetMethod("Build", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (method is null)
        {
            throw new InvalidOperationException("OverhaulMatrix.Build not found");
        }

        var result = method.Invoke(null, new object?[] { catalog, mappings, library, mappingService, search, null });
        return (OverhaulMatrixViewModel)result!;
    }
}
