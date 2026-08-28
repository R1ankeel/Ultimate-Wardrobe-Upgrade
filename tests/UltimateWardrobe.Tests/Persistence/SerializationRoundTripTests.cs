using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Persistence;
using Fixtures = UltimateWardrobe.Tests.Core.Fixtures;

namespace UltimateWardrobe.Tests.Persistence;

/// <summary>
/// Sprint 4.0.4 - Persistence-local JSON serializers round-trip the domain values that will be
/// stored in the <c>project.db</c> JSON columns: the <see cref="CatalogSource"/> kind discriminator
/// and the <see cref="DonorAsset"/>/<see cref="Catalog"/> shapes. <see cref="PersistenceJson"/> is
/// a persistent instance of the Scanner's proven conventions (camelCase + string enums), so these
/// tests are the Persistence-side guarantee that nothing lossy is introduced.
/// </summary>
public class SerializationRoundTripTests
{
    [Fact]
    public void VanillaCatalogSource_RoundTrips()
    {
        var source = Fixtures.CreateVanillaSource("D:/Skymod/Stock Game");
        var json = PersistenceJson.Serialize<CatalogSource>(source);

        var loaded = PersistenceJson.Deserialize<CatalogSource>(json);

        loaded.Should().BeOfType<VanillaCatalogSource>();
        var vanilla = (VanillaCatalogSource)loaded!;
        vanilla.Kind.Should().Be(CatalogSourceKind.VanillaPlusDlc);
        vanilla.RootPath.Should().Be("D:/Skymod/Stock Game");
        vanilla.PluginNames.Should().Equal("Skyrim.esm", "Update.esm");
    }

    [Fact]
    public void StoryModCatalogSource_RoundTrips()
    {
        var source = Fixtures.CreateStorySource();
        var json = PersistenceJson.Serialize<CatalogSource>(source);

        var loaded = PersistenceJson.Deserialize<CatalogSource>(json);

        loaded.Should().BeOfType<StoryModCatalogSource>();
        var story = (StoryModCatalogSource)loaded!;
        story.Kind.Should().Be(CatalogSourceKind.StoryMod);
        story.MainPlugin.Should().Be("Vigilant.esp");
        story.Masters.Should().Equal("Skyrim.esm");
    }

    [Fact]
    public void Overhaul_Source_RoundTrips_Through_The_Converter()
    {
        var project = Fixtures.CreateProject();
        var story = Fixtures.CreateStorySource();
        var overhaul = new Overhaul(Guid.NewGuid(), "Vigilant Overhaul", project.Id, story)
        {
            Policy = PatchPolicy.RequireBodyConversion,
        };

        var json = PersistenceJson.Serialize(overhaul);
        var loaded = PersistenceJson.Deserialize<Overhaul>(json);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Vigilant Overhaul");
        loaded.Policy.Should().Be(PatchPolicy.RequireBodyConversion);
        loaded.Source.Should().BeOfType<StoryModCatalogSource>();
        ((StoryModCatalogSource)loaded.Source).MainPlugin.Should().Be("Vigilant.esp");
    }

    [Fact]
    public void FullReplacerDonor_RoundTrips()
    {
        var donor = Fixtures.CreateDonorAsset(kind: DonorAssetKind.FullReplacer);
        var json = PersistenceJson.Serialize(donor);

        var loaded = PersistenceJson.Deserialize<DonorAsset>(json);

        loaded.Should().NotBeNull();
        loaded!.ImportId.Should().Be(donor.ImportId);
        loaded.OriginalFileName.Should().Be(donor.OriginalFileName);
        loaded.ExtractedPath.Should().Be(donor.ExtractedPath);
        loaded.ArchiveHash.Should().Be(donor.ArchiveHash);
        loaded.Kind.Should().Be(DonorAssetKind.FullReplacer);
        loaded.ProvidedSets.Should().BeEmpty();
        loaded.FileManifest.Should().BeEmpty();
        loaded.DetectedBodySlideFiles.Should().BeEmpty();
        loaded.DetectedPhysicsFiles.Should().BeEmpty();
    }

    [Fact]
    public void BodyConversionPatchDonor_RoundTrips_With_ProvidedSets_And_Manifest()
    {
        var variant = new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("DonorCuirass", 0x00001234, "32 Body", "AA_Donor", "donor/cuirass.nif") });
        var set = new DonorProvidedSet("setA", "Donor Set", new[] { variant });
        var donor = new DonorAsset(
            Guid.NewGuid(),
            "bodypatch.7z",
            $"C:/Project/Source/{Guid.NewGuid()}",
            DateTime.UtcNow,
            "hash456",
            DonorAssetKind.BodyConversionPatch,
            providedSets: new[] { set },
            fileManifest: new[] { new DonorFileEntry("meshes/donor/cuirass.nif", 12345L) },
            detectedBodySlideFiles: new[] { "assets/body/0.nif" },
            detectedPhysicsFiles: new[] { "physics/1.hkx" });

        var json = PersistenceJson.Serialize(donor);
        var loaded = PersistenceJson.Deserialize<DonorAsset>(json);

        loaded.Should().NotBeNull();
        loaded!.ImportId.Should().Be(donor.ImportId);
        loaded.Kind.Should().Be(DonorAssetKind.BodyConversionPatch);
        loaded.DetectedBodySlideFiles.Should().Equal("assets/body/0.nif");
        loaded.DetectedPhysicsFiles.Should().Equal("physics/1.hkx");

        var loadedSet = loaded.ProvidedSets.Should().ContainSingle().Subject;
        loadedSet.Id.Should().Be("setA");
        loadedSet.DisplayName.Should().Be("Donor Set");
        loadedSet.Variants.Should().ContainSingle().Which.Gender.Should().Be(Gender.Female);

        var file = loaded.FileManifest.Should().ContainSingle().Subject;
        file.RelativePath.Should().Be("meshes/donor/cuirass.nif");
        file.Length.Should().Be(12345L);
    }

    [Fact]
    public void Catalog_RoundTrips_Source_And_Sets()
    {
        var story = Fixtures.CreateStorySource("C:/Mods/Vigilant", "Vigilant.esp");
        var set = Fixtures.CreateArmorSet("VigilantRobes", "Vigilant Robes");
        var catalog = new Catalog(story, new[] { set });

        var json = PersistenceJson.Serialize(catalog);
        var loaded = PersistenceJson.Deserialize<Catalog>(json);

        loaded.Should().NotBeNull();
        loaded!.Source.Should().BeOfType<StoryModCatalogSource>();
        ((StoryModCatalogSource)loaded.Source).MainPlugin.Should().Be("Vigilant.esp");
        var loadedSet = loaded.Sets.Should().ContainSingle().Subject;
        loadedSet.Id.Should().Be("VigilantRobes");
        loadedSet.DisplayName.Should().Be("Vigilant Robes");
        loadedSet.Variants.Should().ContainSingle().Which.Weight.Should().Be(WeightClass.Heavy);
    }

    [Fact]
    public void UnknownKind_Json_Throws_JsonException()
    {
        FluentActions
            .Invoking(() => PersistenceJson.Deserialize<CatalogSource>("{\"kind\":\"unknown\",\"rootPath\":\"/x\"}"))
            .Should().Throw<System.Text.Json.JsonException>();
    }
}
