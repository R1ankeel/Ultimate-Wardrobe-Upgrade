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
/// Regression: the Overhaul screen must construct when the Project screen navigates to it (clicking
/// "Open" on an overhaul card resolves the view from DI). The Sprint 6.6.2 crash was a
/// <c>XamlParseException: "Auto,0,0,0" is not a valid value for "Margin"</c> on the popover's "Import
/// patch" button (<c>Margin="Auto,..."</c> - <c>Auto</c> is a Grid length, not a Thickness), so the
/// page could not parse the moment navigation built it; the "Import patch" button sits inside the
/// shared popover panel and was never exercised in the Sprint 6.5-6.6 interaction suite. The
/// regression resolves the view the way the WPF-UI page provider does after
/// <c>OverhaulViewModel.SelectOverhaul</c> sets <see cref="IOverhaulSelection"/>. Runs on a dedicated
/// STA thread.
/// </summary>
public class OverhaulViewBootTests
{
    [Fact]
    public void OverhaulView_builds_when_navigation_opens_it()
    {
        var root = TestHelpers.NewTempDir("UW_OverhaulView_");
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
                    var page = provider.GetRequiredService<INavigationViewPageProvider>().GetPage(typeof(OverhaulView));
                    page.Should().BeOfType<OverhaulView>();
                    ((OverhaulView)page).DataContext.Should().BeOfType<OverhaulViewModel>();
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
                throw new TimeoutException("OverhaulView construction did not finish within 30s.");
            }

            if (captured is not null)
            {
                throw new InvalidOperationException(
                    "OverhaulView must build without crashing when navigation opens it.",
                    captured);
            }
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }
}