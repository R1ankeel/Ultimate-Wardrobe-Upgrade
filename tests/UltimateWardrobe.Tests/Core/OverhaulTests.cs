using FluentAssertions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Tests.Core;

public class OverhaulTests
{
    [Fact]
    public void Overhaul_Policy_Defaults_To_Loose()
    {
        var project = Fixtures.CreateProject();
        var source = Fixtures.CreateVanillaSource();
        var overhaul = new Overhaul(Guid.NewGuid(), "Vanilla", project.Id, source);

        overhaul.Policy.Should().Be(PatchPolicy.Loose);
        overhaul.Mappings.Should().BeEmpty();
    }

    [Fact]
    public void Overhaul_Policy_Can_Be_Set_Via_Initializer()
    {
        var project = Fixtures.CreateProject();
        var source = Fixtures.CreateVanillaSource();
        var overhaul = new Overhaul(Guid.NewGuid(), "Vanilla", project.Id, source) { Policy = PatchPolicy.RequireBoth };

        overhaul.Policy.Should().Be(PatchPolicy.RequireBoth);
    }

    [Fact]
    public void Overhaul_Creates_With_Source_Immutable()
    {
        var project = Fixtures.CreateProject();
        var source = Fixtures.CreateVanillaSource();
        var overhaul = new Overhaul(Guid.NewGuid(), "Vanilla", project.Id, source);

        overhaul.Source.Should().BeSameAs(source);
        overhaul.Mappings.Should().BeEmpty();
        overhaul.Name.Should().Be("Vanilla");
        overhaul.ProjectId.Should().Be(project.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Overhaul_Throws_On_Empty_Name(string name)
    {
        var project = Fixtures.CreateProject();
        var source = Fixtures.CreateVanillaSource();
        var act = () => new Overhaul(Guid.NewGuid(), name, project.Id, source);
        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void Overhaul_Throws_On_Null_Source()
    {
        var project = Fixtures.CreateProject();
        var act = () => new Overhaul(Guid.NewGuid(), "Name", project.Id, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Overhaul_Throws_On_Empty_Ids()
    {
        var source = Fixtures.CreateVanillaSource();
        var act1 = () => new Overhaul(Guid.Empty, "Name", Guid.NewGuid(), source);
        act1.Should().Throw<ArgumentException>();

        var act2 = () => new Overhaul(Guid.NewGuid(), "Name", Guid.Empty, source);
        act2.Should().Throw<ArgumentException>();
    }
}
