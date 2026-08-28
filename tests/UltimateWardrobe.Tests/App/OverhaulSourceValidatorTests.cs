using FluentAssertions;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.2 - <see cref="OverhaulSourceValidator"/>: Vanilla requires the game root with
/// <c>Data\Skyrim.esm</c>; a story mod requires the vanilla base, its main plugin (under Data or the
/// mod root) and - when masters are supplied - every master beside the main plugin.
/// </summary>
public class OverhaulSourceValidatorTests
{
    private readonly OverhaulSourceValidator _validator = new();

    [Fact]
    public void ValidateVanilla_accepts_game_root_with_skyrim_esm()
    {
        var root = TestHelpers.NewTempDir("UW_Val_");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            File.WriteAllText(Path.Combine(root, "Data", "Skyrim.esm"), string.Empty);

            _validator.ValidateVanilla(root).Should().BeEmpty();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public void ValidateVanilla_rejects_missing_root()
    {
        _validator.ValidateVanilla(Path.Combine(Path.GetTempPath(), "does_not_exist_uw"))
            .Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateVanilla_rejects_root_missing_skyrim_esm()
    {
        var root = TestHelpers.NewTempDir("UW_Val_");
        try
        {
            var errors = _validator.ValidateVanilla(root);
            errors.Should().NotBeEmpty();
            errors.Should().Contain(e => e.Contains("Skyrim.esm", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public void ValidateStoryMod_accepts_valid_vanilla_base_and_main_plugin()
    {
        var gameRoot = TestHelpers.NewTempDir("UW_Val_");
        var modRoot = TestHelpers.NewTempDir("UW_ValMod_");
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));
            File.WriteAllText(Path.Combine(gameRoot, "Data", "Skyrim.esm"), string.Empty);
            File.WriteAllText(Path.Combine(modRoot, "Vigilant.esm"), string.Empty);

            _validator.ValidateStoryMod(gameRoot, "Vigilant.esm", modRoot).Should().BeEmpty();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(gameRoot);
            TestHelpers.DeleteDirectoryRetry(modRoot);
        }
    }

    [Fact]
    public void ValidateStoryMod_rejects_missing_main_plugin()
    {
        var gameRoot = TestHelpers.NewTempDir("UW_Val_");
        var modRoot = TestHelpers.NewTempDir("UW_ValMod_");
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));
            File.WriteAllText(Path.Combine(gameRoot, "Data", "Skyrim.esm"), string.Empty);

            var errors = _validator.ValidateStoryMod(gameRoot, "Vigilant.esm", modRoot);
            errors.Should().Contain(e => e.Contains("Vigilant.esm", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(gameRoot);
            TestHelpers.DeleteDirectoryRetry(modRoot);
        }
    }

    [Fact]
    public void ValidateStoryMod_rejects_missing_vanilla_base()
    {
        var gameRoot = TestHelpers.NewTempDir("UW_Val_");
        var modRoot = TestHelpers.NewTempDir("UW_ValMod_");
        try
        {
            File.WriteAllText(Path.Combine(modRoot, "Vigilant.esm"), string.Empty);

            var errors = _validator.ValidateStoryMod(gameRoot, "Vigilant.esm", modRoot);
            errors.Should().Contain(e => e.Contains("Skyrim.esm", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(gameRoot);
            TestHelpers.DeleteDirectoryRetry(modRoot);
        }
    }

    [Fact]
    public void ValidateStoryMod_rejects_missing_master_and_accepts_present_master()
    {
        var gameRoot = TestHelpers.NewTempDir("UW_Val_");
        var modRoot = TestHelpers.NewTempDir("UW_ValMod_");
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));
            File.WriteAllText(Path.Combine(gameRoot, "Data", "Skyrim.esm"), string.Empty);
            File.WriteAllText(Path.Combine(modRoot, "Vigilant.esm"), string.Empty);
            File.WriteAllText(Path.Combine(modRoot, "Skyrim.esm"), string.Empty);

            _validator.ValidateStoryMod(gameRoot, "Vigilant.esm", modRoot, new[] { "Skyrim.esm" })
                .Should().BeEmpty();

            var errors = _validator.ValidateStoryMod(gameRoot, "Vigilant.esm", modRoot, new[] { "MissingMaster.esm" });
            errors.Should().Contain(e => e.Contains("MissingMaster.esm", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(gameRoot);
            TestHelpers.DeleteDirectoryRetry(modRoot);
        }
    }
}
