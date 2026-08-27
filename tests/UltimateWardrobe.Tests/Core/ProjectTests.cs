using FluentAssertions;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Tests.Core;

public class ProjectTests
{
    [Fact]
    public void Project_Creates_With_Library_OneToOne()
    {
        var project = Fixtures.CreateProject();

        project.Library.Should().NotBeNull();
        project.Library.ProjectId.Should().Be(project.Id);
        project.Library.Assets.Should().BeEmpty();
        project.Overhauls.Should().BeEmpty();
        project.SchemaVersion.Should().Be(1);
        project.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Project_Throws_On_Empty_Name(string? name)
    {
        var act = () => new Project(Guid.NewGuid(), name!, "C:/Root");
        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Project_Throws_On_Empty_RootPath(string? root)
    {
        var act = () => new Project(Guid.NewGuid(), "Name", root!);
        act.Should().Throw<ArgumentException>().WithParameterName("rootPath");
    }

    [Fact]
    public void Project_Throws_On_Empty_Id()
    {
        var act = () => new Project(Guid.Empty, "Name", "C:/Root");
        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Project_Has_Separate_Overhauls_Collections()
    {
        var p1 = Fixtures.CreateProject("P1");
        var p2 = Fixtures.CreateProject("P2");
        p1.Library.Should().NotBeSameAs(p2.Library);
        p1.Id.Should().NotBe(p2.Id);
    }
}
