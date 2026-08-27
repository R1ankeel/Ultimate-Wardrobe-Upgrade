using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Core;

public class ArmorSetTests
{
    [Fact]
    public void ArmorSet_Creates_With_Variants_And_Pieces()
    {
        var piece = new Piece("ArmorIronCuirass", 0x12E46, "Body");
        var variant = new Variant(Gender.Male, WeightClass.Heavy, new[] { piece });
        var set = new ArmorSet("IronArmor", "Iron Armor", new[] { variant });

        set.Id.Should().Be("IronArmor");
        set.DisplayName.Should().Be("Iron Armor");
        set.Variants.Should().HaveCount(1);
        set.Variants[0].Pieces.Should().HaveCount(1);
        set.Status.Should().Be(ArmorSetStatus.NotStarted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ArmorSet_Throws_On_Empty_Id(string id)
    {
        var act = () => new ArmorSet(id, "Display", Array.Empty<Variant>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Piece_Throws_On_Empty_EditorId()
    {
        var act = () => new Piece("", 0, "Body");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Piece_Throws_On_Empty_Slot()
    {
        var act = () => new Piece("Editor", 0, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Variant_Requires_Pieces()
    {
        var act = () => new Variant(Gender.Female, WeightClass.Light, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Catalog_Holds_Source_And_Sets()
    {
        var catalog = Fixtures.CreateCatalog();
        catalog.Source.Should().NotBeNull();
        catalog.Sets.Should().HaveCount(1);
        catalog.Warnings.Should().BeEmpty();
        catalog.Stats.Should().NotBeNull();
    }
}
