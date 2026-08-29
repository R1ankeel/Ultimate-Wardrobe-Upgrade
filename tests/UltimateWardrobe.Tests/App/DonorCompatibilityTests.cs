using FluentAssertions;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// F6 - donor compatibility matrix for weight-agnostic, slot-strict logic.
/// </summary>
[Trait("Category", "App")]
public sealed class DonorCompatibilityTests
{
    private static DonorAsset MakeDonor(Gender gender, WeightClass weight, string slot, string meshPath = "meshes/armor/test.nif", string editorId = "TestPiece")
    {
        var piece = new Piece(editorId, 0x1000, slot, "TestArma", meshPath);
        var variant = new Variant(gender, weight, new[] { piece });
        var set = new DonorProvidedSet("testset", "Test", new[] { variant });
        return new DonorAsset(Guid.NewGuid(), "test.7z", "C:/tmp", DateTime.UtcNow, "hash", DonorAssetKind.FullReplacer, new[] { set });
    }

    private static DonorAsset MakeDonorWithTwoSlots(Gender gender, WeightClass weight)
    {
        var body = new Piece("BodyPiece", 0x1001, "32 Body", "ArmaBody", "meshes/armor/body.nif");
        var hands = new Piece("HandsPiece", 0x1002, "33 Hands", "ArmaHands", "meshes/armor/hands.nif");
        var variant = new Variant(gender, weight, new[] { body, hands });
        var set = new DonorProvidedSet("testset", "Test", new[] { variant });
        return new DonorAsset(Guid.NewGuid(), "test2.7z", "C:/tmp", DateTime.UtcNow, "hash2", DonorAssetKind.FullReplacer, new[] { set });
    }

    [Fact]
    public void FemaleHeavy_MaleDonor_Rejected()
    {
        var maleHeavy = MakeDonor(Gender.Male, WeightClass.Heavy, "32 Body");
        DonorCompatibility.IsCompatible(maleHeavy, Gender.Female, WeightClass.Heavy).Should().BeFalse("female target must not match male donor");
        DonorCompatibility.FindDonorPiece(maleHeavy, Gender.Female, WeightClass.Heavy, "32 Body").Should().BeNull();
        DonorCompatibility.IsCompatible(maleHeavy, Gender.Female).Should().BeFalse();
        DonorCompatibility.FindDonorPiece(maleHeavy, Gender.Female, "32 Body").Should().BeNull();
    }

    [Fact]
    public void FemaleHeavy_FemaleDonor_AnyWeight_Accepted_WhenSlotHits()
    {
        var femaleAny = MakeDonor(Gender.Female, WeightClass.Any, "32 Body");
        var femaleLight = MakeDonor(Gender.Female, WeightClass.Light, "32 Body", "meshes/armor/light.nif");
        var femaleHeavy = MakeDonor(Gender.Female, WeightClass.Heavy, "32 Body");
        var femaleClothing = MakeDonor(Gender.Female, WeightClass.Clothing, "32 Body");

        // Weight is ignored - all should be compatible for Female Heavy target when slot matches
        DonorCompatibility.IsCompatible(femaleAny, Gender.Female, WeightClass.Heavy).Should().BeTrue();
        DonorCompatibility.IsCompatible(femaleLight, Gender.Female, WeightClass.Heavy).Should().BeTrue();
        DonorCompatibility.IsCompatible(femaleHeavy, Gender.Female, WeightClass.Heavy).Should().BeTrue();
        DonorCompatibility.IsCompatible(femaleClothing, Gender.Female, WeightClass.Heavy).Should().BeTrue();

        // Gender-only overload same
        DonorCompatibility.IsCompatible(femaleAny, Gender.Female).Should().BeTrue();

        // Slot hit
        DonorCompatibility.FindDonorPiece(femaleAny, Gender.Female, WeightClass.Heavy, "32 Body").Should().NotBeNull();
        DonorCompatibility.FindDonorPiece(femaleLight, Gender.Female, WeightClass.Heavy, "32 Body")!.Slot.Should().Be("32 Body");
        // Clothing donor backing Heavy target - the F6 clothing 1-piece case
        DonorCompatibility.FindDonorPiece(femaleClothing, Gender.Female, WeightClass.Heavy, "32 Body").Should().NotBeNull();
        DonorCompatibility.FindDonorPiece(femaleClothing, Gender.Female, "32 Body").Should().NotBeNull();
    }

    [Fact]
    public void SlotMismatch_ReturnsNull()
    {
        var femaleBodyOnly = MakeDonor(Gender.Female, WeightClass.Heavy, "32 Body");
        // Target wants Hands, donor only has Body - must return null, not fallback to Body
        DonorCompatibility.FindDonorPiece(femaleBodyOnly, Gender.Female, WeightClass.Heavy, "33 Hands").Should().BeNull();
        DonorCompatibility.FindDonorPiece(femaleBodyOnly, Gender.Female, "33 Hands").Should().BeNull();

        // Even with weight Any, still slot-strict
        var femaleBodyAny = MakeDonor(Gender.Female, WeightClass.Any, "32 Body");
        DonorCompatibility.FindDonorPiece(femaleBodyAny, Gender.Female, WeightClass.Heavy, "33 Hands").Should().BeNull();
    }

    [Fact]
    public void Branch2_Word_Cuirass_Matches_Frozen_32Body()
    {
        var wordDonor = MakeDonor(Gender.Female, WeightClass.Any, "Cuirass", "meshes/armor/cuirass.nif");
        DonorCompatibility.IsCompatible(wordDonor, Gender.Female).Should().BeTrue();
        DonorCompatibility.FindDonorPiece(wordDonor, Gender.Female, "32 Body").Should().NotBeNull();
        DonorCompatibility.FindDonorPiece(wordDonor, Gender.Female, WeightClass.Clothing, "32 Body").Should().NotBeNull();
        // Reverse: frozen donor backing word target (e.g., vanilla Body vs mesh-only Robe)
        var frozenDonor = MakeDonor(Gender.Female, WeightClass.Any, "32 Body");
        DonorCompatibility.FindDonorPiece(frozenDonor, Gender.Female, "Cuirass").Should().NotBeNull();
        // Robe word also maps to Body
        var robeDonor = MakeDonor(Gender.Female, WeightClass.Any, "Robe", "meshes/armor/robe.nif");
        DonorCompatibility.FindDonorPiece(robeDonor, Gender.Female, "32 Body").Should().NotBeNull();
    }

    [Fact]
    public void FallbackNoLongerMisfires_BodyDoesNotBackHands()
    {
        var bodyOnly = MakeDonor(Gender.Female, WeightClass.Heavy, "32 Body");
        // Old fallback would return Body piece for Hands target - F2 forbids it
        DonorCompatibility.FindDonorPiece(bodyOnly, Gender.Female, "33 Hands").Should().BeNull("fallback to first piece must not happen");
        DonorCompatibility.FindDonorPiece(bodyOnly, Gender.Female, "37 Feet").Should().BeNull();
        DonorCompatibility.FindDonorPiece(bodyOnly, Gender.Female, "31 Hair").Should().BeNull();

        // Donor with Body+Hands correctly backs both slots, but still strict
        var full = MakeDonorWithTwoSlots(Gender.Female, WeightClass.Heavy);
        DonorCompatibility.FindDonorPiece(full, Gender.Female, "32 Body")!.EditorId.Should().Be("BodyPiece");
        DonorCompatibility.FindDonorPiece(full, Gender.Female, "33 Hands")!.EditorId.Should().Be("HandsPiece");
        DonorCompatibility.FindDonorPiece(full, Gender.Female, "37 Feet").Should().BeNull();
    }

    [Fact]
    public void UnisexMatchesBothGenders_WeightStillIgnored()
    {
        var unisexAny = MakeDonor(Gender.Unisex, WeightClass.Any, "32 Body");
        DonorCompatibility.IsCompatible(unisexAny, Gender.Female).Should().BeTrue();
        DonorCompatibility.IsCompatible(unisexAny, Gender.Male).Should().BeTrue();
        DonorCompatibility.IsCompatible(unisexAny, Gender.Female, WeightClass.Heavy).Should().BeTrue();
        DonorCompatibility.IsCompatible(unisexAny, Gender.Male, WeightClass.Light).Should().BeTrue();

        DonorCompatibility.FindDonorPiece(unisexAny, Gender.Female, "32 Body").Should().NotBeNull();
        DonorCompatibility.FindDonorPiece(unisexAny, Gender.Male, "32 Body").Should().NotBeNull();
    }

    [Fact]
    public void DonorContainsBody_StrictPerGender()
    {
        var female3ba = MakeDonor(Gender.Female, WeightClass.Heavy, "32 Body", "meshes/armor/3ba/body.nif");
        var maleHimbo = MakeDonor(Gender.Male, WeightClass.Heavy, "32 Body", "meshes/armor/himbo/body.nif");
        var genericBodySlide = new DonorAsset(Guid.NewGuid(), "generic.7z", "C:/tmp", DateTime.UtcNow, "h", DonorAssetKind.FullReplacer,
            new[] { new DonorProvidedSet("s", "S", new[] { new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece("P", 0, "32 Body", "A", "meshes/armor/plain.nif") }) }) },
            detectedBodySlideFiles: new[] { "CalienteTools/BodySlide/SliderSets/Generic.osp" });

        DonorCompatibility.DonorContainsBody(female3ba, BodyType.ThreeBA).Should().BeTrue();
        DonorCompatibility.DonorContainsBody(female3ba, BodyType.HIMBO).Should().BeFalse();

        DonorCompatibility.DonorContainsBody(maleHimbo, BodyType.HIMBO).Should().BeTrue();
        DonorCompatibility.DonorContainsBody(maleHimbo, BodyType.ThreeBA).Should().BeFalse();

        // Generic BodySlide without marker does not satisfy either
        DonorCompatibility.DonorContainsBody(genericBodySlide, BodyType.ThreeBA).Should().BeFalse("generic BodySlide without 3ba token must not count for 3BA");
        DonorCompatibility.DonorContainsBody(genericBodySlide, BodyType.HIMBO).Should().BeFalse();

        DonorCompatibility.DonorHasPhysics(maleHimbo).Should().BeFalse();
        var withPhysics = MakeDonor(Gender.Female, WeightClass.Heavy, "32 Body", "meshes/armor/body.nif");
        withPhysics = new DonorAsset(withPhysics.ImportId, withPhysics.OriginalFileName, withPhysics.ExtractedPath, withPhysics.ImportedAt, withPhysics.ArchiveHash, withPhysics.Kind, withPhysics.ProvidedSets, detectedPhysicsFiles: new[] { "physics/hdt.xml" });
        DonorCompatibility.DonorHasPhysics(withPhysics).Should().BeTrue();
    }
}
