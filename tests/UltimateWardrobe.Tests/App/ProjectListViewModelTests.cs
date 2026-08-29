using FluentAssertions;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Storage;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Persistence;
using UltimateWardrobe.Tests.Persistence;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Sprint 6.2 - <see cref="ProjectListViewModel"/> picker flow with a real <see cref="ProjectStoreFactory"/>
/// and <see cref="ProjectSession"/>: new project genuinely creates <c>project.db</c> + records recent,
/// opening an existing db resolves the session, a missing db on "open" alerts and leaves the session
/// untouched, and a cancelled folder dialog does nothing. Recent list load of the settings file, the
/// delete-project command (confirms, removes the folder from disk and forgets the recent entry), and
/// a reflection guarantee that no switch/close project command is exposed are also covered.
/// </summary>
[Trait("Category", "App")]
public class ProjectListViewModelTests
{
    [Fact]
    public async Task NewProject_creates_db_records_recent_and_requests_close()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var session = new ProjectSession(new ProjectStoreFactory());
            var dialogs = new ScriptedDialogService();
            var vm = new ProjectListViewModel(
                new RecentProjectsStore(Path.Combine(root, "settings.json")),
                new ProjectStoreFactory(),
                session,
                dialogs);

            var closed = 0;
            vm.CloseRequested += () => closed++;

            await vm.OpenRootAsync(root, createIfMissing: true);

            File.Exists(Path.Combine(root, "project.db")).Should().BeTrue("a real project.db must be created");
            session.IsOpen.Should().BeTrue();
            session.Project!.RootPath.Should().Be(Path.GetFullPath(root));
            vm.RecentProjects.Should().ContainSingle(p => p.Path == Path.Combine(root, "project.db"));
            closed.Should().Be(1);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task OpenExisting_resolves_session_from_existing_db()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var dbPath = Path.Combine(root, "project.db");
            var original = new Project(Guid.NewGuid(), "Existing", root);
            var store = new ProjectStore(dbPath);
            await store.SaveAsync(original);

            var session = new ProjectSession(new ProjectStoreFactory());
            var vm = new ProjectListViewModel(
                new RecentProjectsStore(Path.Combine(root, "settings.json")),
                new ProjectStoreFactory(),
                session,
                new ScriptedDialogService());

            var closed = 0;
            vm.CloseRequested += () => closed++;

            await vm.OpenRootAsync(root, createIfMissing: false);

            session.IsOpen.Should().BeTrue();
            session.Project!.Name.Should().Be("Existing");
            session.Project!.Id.Should().Be(original.Id);
            vm.RecentProjects.Should().ContainSingle(p => p.Path == dbPath);
            closed.Should().Be(1);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task OpenMissingDb_alerts_and_leaves_session_untouched()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var session = new ProjectSession(new ProjectStoreFactory());
            var dialogs = new ScriptedDialogService();
            var vm = new ProjectListViewModel(
                new RecentProjectsStore(Path.Combine(root, "settings.json")),
                new ProjectStoreFactory(),
                session,
                dialogs);

            var closed = 0;
            vm.CloseRequested += () => closed++;

            await vm.OpenRootAsync(root, createIfMissing: false);

            dialogs.Alerts.Should().NotBeEmpty();
            session.IsOpen.Should().BeFalse();
            closed.Should().Be(0);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task CancelledFolderDialog_does_nothing()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var session = new ProjectSession(new ProjectStoreFactory());
            var dialogs = new ScriptedDialogService();
            dialogs.PickProjectFolder = (_, _) => null;
            var vm = new ProjectListViewModel(
                new RecentProjectsStore(Path.Combine(root, "settings.json")),
                new ProjectStoreFactory(),
                session,
                dialogs);

            var closed = 0;
            vm.CloseRequested += () => closed++;

            await vm.NewProjectCommand.ExecuteAsync(null);

            session.IsOpen.Should().BeFalse();
            closed.Should().Be(0);
            vm.RecentProjects.Should().BeEmpty();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_loads_recent_entries_in_order()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var recent = new RecentProjectsStore(Path.Combine(root, "settings.json"));
            var a = Path.Combine(root, "projA", "project.db");
            var b = Path.Combine(root, "projB", "project.db");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            recent.AddRecentProject(b);
            recent.AddRecentProject(a);

            var vm = new ProjectListViewModel(
                recent,
                new ProjectStoreFactory(),
                new ProjectSession(new ProjectStoreFactory()),
                new ScriptedDialogService());

            await vm.InitializeAsync();

            vm.RecentProjects.Select(p => p.Path).Should().Equal(a, b);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task DeleteRecentCommand_after_confirm_deletes_folder_and_recent_entry()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var path = Path.Combine(root, "projA", "project.db");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "project.db"), string.Empty);
            var projectDir = Path.GetDirectoryName(path)!;

            var recent = new RecentProjectsStore(Path.Combine(root, "settings.json"));
            recent.AddRecentProject(path);

            var dialogs = new ScriptedDialogService { ConfirmResult = true };
            var vm = new ProjectListViewModel(
                recent,
                new ProjectStoreFactory(),
                new ProjectSession(new ProjectStoreFactory()),
                dialogs);
            await vm.InitializeAsync();

            Directory.Exists(projectDir).Should().BeTrue();
            await vm.DeleteRecentCommand.ExecuteAsync(vm.RecentProjects.Single());

            vm.RecentProjects.Should().BeEmpty();
            recent.GetRecentProjectPaths().Should().BeEmpty();
            Directory.Exists(projectDir).Should().BeFalse("the project folder is removed from disk");
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task DeleteRecentCommand_when_not_confirmed_keeps_folder_and_entry()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var path = Path.Combine(root, "projA", "project.db");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "project.db"), string.Empty);
            var projectDir = Path.GetDirectoryName(path)!;

            var recent = new RecentProjectsStore(Path.Combine(root, "settings.json"));
            recent.AddRecentProject(path);

            var dialogs = new ScriptedDialogService { ConfirmResult = false };
            var vm = new ProjectListViewModel(
                recent,
                new ProjectStoreFactory(),
                new ProjectSession(new ProjectStoreFactory()),
                dialogs);
            await vm.InitializeAsync();

            await vm.DeleteRecentCommand.ExecuteAsync(vm.RecentProjects.Single());

            dialogs.Confirms.Should().ContainSingle();
            vm.RecentProjects.Should().ContainSingle();
            Directory.Exists(projectDir).Should().BeTrue("no folder is removed without confirmation");
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task OpenRecent_existing_db_resolves_session_from_folder()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var projectDir = Path.Combine(root, "projA");
            Directory.CreateDirectory(projectDir);
            var dbPath = Path.Combine(projectDir, "project.db");
            var original = new Project(Guid.NewGuid(), "projA", projectDir);
            await new ProjectStore(dbPath).SaveAsync(original);

            var recent = new RecentProjectsStore(Path.Combine(root, "settings.json"));
            recent.AddRecentProject(dbPath);

            var session = new ProjectSession(new ProjectStoreFactory());
            var vm = new ProjectListViewModel(
                recent,
                new ProjectStoreFactory(),
                session,
                new ScriptedDialogService());
            await vm.InitializeAsync();

            var closed = 0;
            vm.CloseRequested += () => closed++;
            vm.SelectedRecent = vm.RecentProjects.Single();

            await vm.OpenSelectedRecentCommand.ExecuteAsync(null);

            session.IsOpen.Should().BeTrue();
            session.Project!.Id.Should().Be(original.Id);
            session.Project!.RootPath.Should().Be(Path.GetFullPath(projectDir));
            closed.Should().Be(1);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public async Task OpenRecent_missing_db_recreates_db_and_opens()
    {
        var root = TestHelpers.NewTempDir("UW_List_");
        try
        {
            var projectDir = Path.Combine(root, "projA");
            Directory.CreateDirectory(projectDir);
            var dbPath = Path.Combine(projectDir, "project.db");
            File.Exists(dbPath).Should().BeFalse("the missing database is the point of this test");

            var recent = new RecentProjectsStore(Path.Combine(root, "settings.json"));
            recent.AddRecentProject(dbPath);

            var session = new ProjectSession(new ProjectStoreFactory());
            var vm = new ProjectListViewModel(
                recent,
                new ProjectStoreFactory(),
                session,
                new ScriptedDialogService());
            await vm.InitializeAsync();

            var closed = 0;
            vm.CloseRequested += () => closed++;
            vm.SelectedRecent = vm.RecentProjects.Single();

            await vm.OpenSelectedRecentCommand.ExecuteAsync(null);

            File.Exists(dbPath).Should().BeTrue("opening a recent project without a db recreates it");
            session.IsOpen.Should().BeTrue();
            session.Project!.RootPath.Should().Be(Path.GetFullPath(projectDir));
            session.Project!.Name.Should().Be("projA");
            closed.Should().Be(1);
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    [Fact]
    public void No_switch_or_close_project_command_is_exposed()
    {
        CommandNameLeak.Check(typeof(ProjectListViewModel));
    }
}
