using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Core;

public class CatalogSourceTests
{
    [Fact]
    public void VanillaSource_Has_Correct_Kind()
    {
        var s = Fixtures.CreateVanillaSource("C:/Game");
        s.Kind.Should().Be(CatalogSourceKind.VanillaPlusDlc);
        s.RootPath.Should().Be("C:/Game");
        s.PluginNames.Should().Contain("Skyrim.esm");
    }

    [Fact]
    public void StoryModSource_Has_Correct_Kind_And_MainPlugin()
    {
        var s = Fixtures.CreateStorySource("C:/Mods/Vigilant", "Vigilant.esp");
        s.Kind.Should().Be(CatalogSourceKind.StoryMod);
        s.MainPlugin.Should().Be("Vigilant.esp");
        s.Masters.Should().Contain("Skyrim.esm");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CatalogSource_Throws_On_Empty_Root(string root)
    {
        var act = () => new VanillaCatalogSource(root);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StoryMod_Throws_On_Empty_MainPlugin()
    {
        var act = () => new StoryModCatalogSource("C:/Root", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Polymorphism_Dispatches_By_Kind()
    {
        CatalogSource vanilla = Fixtures.CreateVanillaSource();
        CatalogSource story = Fixtures.CreateStorySource();

        vanilla.Should().BeOfType<VanillaCatalogSource>();
        story.Should().BeOfType<StoryModCatalogSource>();
    }
}
