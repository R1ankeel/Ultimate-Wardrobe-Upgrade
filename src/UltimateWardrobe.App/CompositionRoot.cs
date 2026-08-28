using Microsoft.Extensions.DependencyInjection;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Storage;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Mapping;
using UltimateWardrobe.Patcher;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.App;

/// <summary>
/// Single composition root for the WPF shell (Phase 6 Sprint 6.1). <see cref="Register"/> wires
/// infrastructure, app services, domain services and view models; WPF-UI services, adapters and
/// views are added only when <paramref name="registerUi"/> is true so headless tests can build the
/// non-UI graph (amendment 2 - VM-to-UI adapter interfaces). A guarded call registers a marker and
/// throws if called twice for the same <see cref="IServiceCollection"/>.
/// </summary>
public static class CompositionRoot
{
    public static void Register(IServiceCollection services, bool registerUi = true)
    {
        if (services.Any(d => d.ServiceType == typeof(CompositionRootMarker)))
        {
            throw new InvalidOperationException(
                "CompositionRoot.Register has already been called for this service collection.");
        }
        services.AddSingleton<CompositionRootMarker>();

        services.AddSingleton<ILogViewer, LogViewer>();
        services.AddSingleton<RecentProjectsStore>();
        services.AddSingleton<IProjectSession, ProjectSession>();
        services.AddSingleton<IProjectStoreFactory, ProjectStoreFactory>();
        services.AddSingleton<IOverhaulSourceValidator, OverhaulSourceValidator>();
        services.AddSingleton<IBackgroundTaskService, DispatcherBackgroundTaskService>();

        services.AddSingleton(_ => new CompositeExtractor());
        services.AddSingleton<IArchiveExtractor>(sp => sp.GetRequiredService<CompositeExtractor>());
        services.AddTransient<FolderCatalogScanner>();
        services.AddTransient<UltimateWardrobe.DonorLibrary.DonorClassifier>();
        services.AddTransient<DonorImportService>();
        services.AddTransient<UltimateWardrobe.DonorLibrary.DonorLibraryService>();
        services.AddTransient<IPatcher, WardrobePatcher>();
        services.AddTransient(sp =>
        {
            var session = sp.GetRequiredService<IProjectSession>();
            if (!session.IsOpen)
            {
                throw new InvalidOperationException(
                    "No project is open. MappingService cannot be created until a project is selected.");
            }
            return new MappingService(session.Project!.Library);
        });

        services.AddTransient<ProjectListViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<ProjectViewModel>();
        services.AddTransient<OverhaulViewModel>();
        services.AddTransient<ArmorSetDetailViewModel>();
        services.AddTransient<DonorLibraryViewModel>();
        services.AddTransient<ExportViewModel>();

        if (!registerUi)
        {
            services.AddSingleton<IAppNavigationService, NullAppNavigationService>();
            services.AddSingleton<IAppDialogService, NullAppDialogService>();
            services.AddSingleton<ISnackbarService, NullSnackbarService>();
            return;
        }

        services.AddSingleton<Wpf.Ui.Abstractions.INavigationViewPageProvider, AppNavigationViewPageProvider>();
        services.AddSingleton<Wpf.Ui.INavigationService>(sp =>
            new Wpf.Ui.NavigationService(sp.GetRequiredService<Wpf.Ui.Abstractions.INavigationViewPageProvider>()));
        services.AddSingleton<Wpf.Ui.IContentDialogService, Wpf.Ui.ContentDialogService>();
        services.AddSingleton<Wpf.Ui.ISnackbarService, Wpf.Ui.SnackbarService>();
        services.AddSingleton<IAppNavigationService, AppNavigationService>();
        services.AddSingleton<IAppDialogService, WpfUiDialogService>();
        services.AddSingleton<ISnackbarService, WpfUiSnackbarService>();

        services.AddTransient<MainWindow>();
        services.AddTransient<ProjectPickerWindow>();
        services.AddTransient<ProjectView>();
        services.AddTransient<OverhaulView>();
        services.AddTransient<ExportView>();
    }

    private sealed class CompositionRootMarker
    {
    }
}