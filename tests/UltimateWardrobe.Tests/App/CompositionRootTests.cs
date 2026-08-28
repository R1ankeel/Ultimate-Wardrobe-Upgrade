using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using UltimateWardrobe.App;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Storage;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Mapping;
using UltimateWardrobe.Patcher;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// DI smoke test (Phase 6 Sprint 6.1): the headless composition root (<c>registerUi=false</c>)
/// resolves every view model and app service - including <see cref="ProjectListViewModel"/> and the
/// picker's services - with no WPF control ever constructed. A second <see cref="CompositionRoot.Register"/>
/// call on the same collection (duplicate registration) and a resolve of an unregistered service
/// (missing service) both fail as expected.
/// </summary>
[Trait("Category", "Di")]
public class CompositionRootTests
{
    [Fact]
    public void Register_headless_resolves_every_viewmodel_and_app_service()
    {
        var services = new ServiceCollection();
        CompositionRoot.Register(services, registerUi: false);

        using var provider = services.BuildServiceProvider();

        using var temp = new TempProjectTestEnv();
        provider.GetRequiredService<IProjectSession>().Open(temp.Project, temp.DatabasePath);

        foreach (var type in AppServices())
        {
            provider.GetRequiredService(type).Should().NotBeNull($"{type.Name} must resolve headless");
        }
    }

    [Fact]
    public void Register_twice_on_same_collection_throws_duplicate_registration()
    {
        var services = new ServiceCollection();
        CompositionRoot.Register(services, registerUi: false);

        var act = () => CompositionRoot.Register(services, registerUi: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been called*");
    }

    [Fact]
    public void Resolving_an_unregistered_service_throws_missing_service()
    {
        var services = new ServiceCollection();
        CompositionRoot.Register(services, registerUi: false);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService(typeof(IUnregisteredService));

        act.Should().Throw<InvalidOperationException>();
    }

    private static IReadOnlyList<Type> AppServices() => new[]
    {
        // App services
        typeof(ILogViewer),
        typeof(RecentProjectsStore),
        typeof(IProjectSession),
        typeof(IProjectStoreFactory),
        typeof(IOverhaulSourceValidator),
        typeof(IBackgroundTaskService),
        typeof(IAppNavigationService),
        typeof(IAppDialogService),
        typeof(ISnackbarService),
        // View models
        typeof(ProjectListViewModel),
        typeof(MainViewModel),
        typeof(ProjectViewModel),
        typeof(OverhaulViewModel),
        typeof(ArmorSetDetailViewModel),
        typeof(DonorLibraryViewModel),
        typeof(ExportViewModel),
        // Domain services wired through the root
        typeof(CompositeExtractor),
        typeof(IArchiveExtractor),
        typeof(FolderCatalogScanner),
        typeof(DonorClassifier),
        typeof(DonorImportService),
        typeof(DonorLibraryService),
        typeof(IPatcher),
        typeof(MappingService),
    };

    private interface IUnregisteredService
    {
    }

    private sealed class TempProjectTestEnv : IDisposable
    {
        public TempProjectTestEnv()
        {
            Root = Path.Combine(Path.GetTempPath(), "UW_Di_Smoke_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Project = new Project(Guid.NewGuid(), "Smoke", Root);
            DatabasePath = Path.Combine(Root, "project.db");
        }

        public string Root { get; }

        public Project Project { get; }

        public string DatabasePath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
