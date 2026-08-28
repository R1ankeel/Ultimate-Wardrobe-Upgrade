using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using UltimateWardrobe.App;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.ViewModels;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Tests.Persistence;
using Wpf.Ui.Abstractions;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// Regression: the Export screen must construct when the sheet navigates to it (the Sprint 6.7 crash
/// was not on navigation but on the first Build click - snackbar calls threw
/// "<c>The SnackbarPresenter was never set</c>" because the shell never attached a
/// <see cref="Wpf.Ui.Controls.SnackbarPresenter"/>). This resolves the view the way the WPF-UI page
/// provider does, on a dedicated STA thread, with the shell DI so the snackbar wiring is present.
/// Serialized with the other WPF boot tests.
/// </summary>
[Collection("WPF Boot Tests")]
public class ExportViewBootTests
{
    [Fact]
    public void ExportView_builds_when_navigation_opens_it()
    {
        var root = TestHelpers.NewTempDir("UW_ExportView_");
        try
        {
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var services = new ServiceCollection();
                    CompositionRoot.Register(services, registerUi: true);
                    using var provider = services.BuildServiceProvider();

                    var catalog = new Catalog(
                        new VanillaCatalogSource("C:/Game"),
                        new[]
                        {
                            new ArmorSet(
                                "IronArmor",
                                "Iron Armor",
                                new Variant[]
                                {
                                    new(
                                        Gender.Male,
                                        WeightClass.Heavy,
                                        new[] { new Piece("IronCuirassM", 0x12345678, "32 Body", "IronCuirassMArma", "armor/iron.nif") }),
                                    new(
                                        Gender.Female,
                                        WeightClass.Heavy,
                                        new[] { new Piece("IronCuirassF", 0x12345679, "32 Body", "IronCuirassFArma", "armor/iron.nif") }),
                                }),
                        });

                    var project = new Project(Guid.NewGuid(), "Boot", root);
                    var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game"))
                    {
                        Catalog = catalog,
                    };
                    project.Overhauls.Add(overhaul);

                    var db = Path.Combine(root, "project.db");
                    provider.GetRequiredService<IProjectSession>().Open(project, db);
                    provider.GetRequiredService<IOverhaulSelection>().Select(overhaul.Id);

                    // Same resolution path as AppNavigationViewPageProvider.GetPage during navigation.
                    var page = provider.GetRequiredService<INavigationViewPageProvider>().GetPage(typeof(ExportView));
                    page.Should().BeOfType<ExportView>();
                    ((ExportView)page).DataContext.Should().BeOfType<ExportViewModel>();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var finished = thread.Join(30000);

            if (!finished)
            {
                throw new TimeoutException("ExportView construction did not finish within 30s.");
            }

            if (captured is not null)
            {
                throw new InvalidOperationException(
                    "ExportView must build without crashing when navigation opens it.",
                    captured);
            }
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }
}