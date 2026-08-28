using FluentAssertions;
using UltimateWardrobe.App.Storage;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.2 - <see cref="RecentProjectsStore"/> over a temp <c>settings.json</c>: add/order (newest
/// first), top-N cap, dedup by full path, remove, and degrade-to-empty on a corrupt/unreadable file.
/// </summary>
public class RecentProjectsStoreTests
{
    [Fact]
    public void Empty_missing_file_returns_empty()
    {
        var dir = TestHelpers.NewTempDir("UW_Rec_");
        try
        {
            var store = new RecentProjectsStore(Path.Combine(dir, "settings.json"));
            store.GetRecentProjectPaths().Should().BeEmpty();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(dir);
        }
    }

    [Fact]
    public void Add_orders_newest_first_and_deduplicates_by_full_path()
    {
        var dir = TestHelpers.NewTempDir("UW_Rec_");
        try
        {
            var store = new RecentProjectsStore(Path.Combine(dir, "settings.json"));
            var a = Path.Combine(dir, "projA", "project.db");
            var b = Path.Combine(dir, "projB", "project.db");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);

            store.AddRecentProject(b);
            store.AddRecentProject(a);
            store.AddRecentProject(a);

            store.GetRecentProjectPaths().Should().Equal(a, b);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(dir);
        }
    }

    [Fact]
    public void Add_caps_at_eight_entries()
    {
        var dir = TestHelpers.NewTempDir("UW_Rec_");
        try
        {
            var store = new RecentProjectsStore(Path.Combine(dir, "settings.json"));
            for (var i = 0; i < 12; i++)
            {
                var path = Path.Combine(dir, $"proj{i}", "project.db");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                store.AddRecentProject(path);
            }

            store.GetRecentProjectPaths().Should().HaveCount(8);
            store.GetRecentProjectPaths().First().Should().EndWith("proj11\\project.db");
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(dir);
        }
    }

    [Fact]
    public void Remove_drops_exactly_the_matching_path()
    {
        var dir = TestHelpers.NewTempDir("UW_Rec_");
        try
        {
            var store = new RecentProjectsStore(Path.Combine(dir, "settings.json"));
            var a = Path.Combine(dir, "projA", "project.db");
            var b = Path.Combine(dir, "projB", "project.db");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);

            store.AddRecentProject(b);
            store.AddRecentProject(a);
            store.RemoveRecentProject(b);

            store.GetRecentProjectPaths().Should().Equal(a);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(dir);
        }
    }

    [Fact]
    public void Corrupt_file_degrades_to_empty()
    {
        var dir = TestHelpers.NewTempDir("UW_Rec_");
        try
        {
            var settings = Path.Combine(dir, "settings.json");
            File.WriteAllText(settings, "{{{ not json");
            var store = new RecentProjectsStore(settings);

            store.GetRecentProjectPaths().Should().BeEmpty();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(dir);
        }
    }
}
