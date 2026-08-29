using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UltimateWardrobe.App;
using UltimateWardrobe.App.Services;
using UltimateWardrobe.App.Views;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Tests.Persistence;
using Wpf.Ui.Abstractions;

namespace UltimateWardrobe.Tests.App;

/// <summary>
/// E3 - UI smoke for virtualization after Phase B.
/// Asserts ListView virtualization is enabled and large catalog opens.
/// </summary>
[Collection("WPF Boot Tests")]
public class OverhaulViewVirtualizationTests
{
    [Fact]
    public void OverhaulView_ListView_is_virtualizing_and_large_catalog_opens()
    {
        var root = TestHelpers.NewTempDir("UW_Virtual_");
        try
        {
            Exception? captured = null;
            ListView? foundListView = null;
            OverhaulMatrixViewModelStats? stats = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var services = new ServiceCollection();
                    CompositionRoot.Register(services, registerUi: true);
                    using var provider = services.BuildServiceProvider();

                    var catalog = CreateSyntheticCatalog(3000);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var project = new Project(Guid.NewGuid(), "Boot", root);
                    var overhaul = new Overhaul(Guid.NewGuid(), "Iron", project.Id, new VanillaCatalogSource("C:/Game")) { Catalog = catalog };
                    project.Overhauls.Add(overhaul);

                    var db = Path.Combine(root, "project.db");
                    provider.GetRequiredService<IProjectSession>().Open(project, db);
                    provider.GetRequiredService<IOverhaulSelection>().Select(overhaul.Id);

                    var page = (OverhaulView)provider.GetRequiredService<INavigationViewPageProvider>().GetPage(typeof(OverhaulView))!;

                    // Force layout so ListView is created
                    var window = new Window { Content = page, Width = 1200, Height = 800, Visibility = Visibility.Hidden };
                    window.Show();
                    window.UpdateLayout();
                    page.UpdateLayout();

                    // Find ListView descendant
                    foundListView = FindDescendant<ListView>(page);
                    foundListView.Should().NotBeNull("OverhaulView must host a virtualizing ListView, not a ScrollViewer+ItemsControl");

                    var lv = foundListView!;
                    var isVirtualizing = VirtualizingPanel.GetIsVirtualizing(lv);
                    var mode = VirtualizingPanel.GetVirtualizationMode(lv);
                    var canContentScroll = ScrollViewer.GetCanContentScroll(lv);
                    var scrollUnit = VirtualizingPanel.GetScrollUnit(lv);

                    isVirtualizing.Should().BeTrue("B3 requires VirtualizingPanel.IsVirtualizing=True");
                    mode.Should().Be(VirtualizationMode.Recycling, "B3 requires Recycling");
                    canContentScroll.Should().BeTrue("B3 requires CanContentScroll=True for virtualization");
                    scrollUnit.Should().Be(ScrollUnit.Pixel, "B3 ScrollUnit should be Pixel for smooth virtualization");

                    // Verify large catalog still builds and virtualizes - check ViewModel stats
                    var vm = (UltimateWardrobe.App.ViewModels.OverhaulViewModel)page.DataContext;
                    // Trigger refresh to ensure MatrixItems built (page Loaded already called Refresh, but ensure)
                    vm.Refresh();
                    stats = new OverhaulMatrixViewModelStats
                    {
                        MatrixItemsCount = vm.MatrixItems.Count,
                        SectionsCount = vm.Sections.Count,
                        ColumnsCount = vm.Columns.Count,
                    };

                    // 3000 sets with unisex etc will produce ~ 3000*~1.5 rows + 2 headers = ~4500 items, but only ~30 realized
                    // We assert build completed (no freeze) and items count is as expected
                    stats.MatrixItemsCount.Should().BeGreaterThan(3000);
                    stats.ColumnsCount.Should().BeGreaterThan(0);

                    sw.Stop();
                    // Large catalog Build plus view construction should be fast (<150 ms for Build alone, <1s for full view)
                    // We assert sw < 2s to avoid flake on CI
                    sw.ElapsedMilliseconds.Should().BeLessThan(2000);

                    window.Close();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var finished = thread.Join(30000);
            if (!finished) throw new TimeoutException("Virtualization test did not finish within 30s.");
            if (captured is not null) throw new InvalidOperationException("Virtualization test failed.", captured);

            foundListView.Should().NotBeNull();
            stats.Should().NotBeNull();
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(root);
        }
    }

    private static Catalog CreateSyntheticCatalog(int count)
    {
        var sets = new List<ArmorSet>(count);
        for (var i = 0; i < count; i++)
        {
            var id = $"Set{i:D4}";
            var name = $"Armor Set {i}";
            var variants = new List<Variant>();
            if (i % 3 == 0)
            {
                variants.Add(new Variant(Gender.Female, WeightClass.Heavy, new[] { new Piece($"Piece{i}_F_H", (uint)(0x10000000 + i), "32 Body", $"Arma{i}_F_H", $"armor/set{i}_f_h.nif") }));
                variants.Add(new Variant(Gender.Male, WeightClass.Heavy, new[] { new Piece($"Piece{i}_M_H", (uint)(0x20000000 + i), "32 Body", $"Arma{i}_M_H", $"armor/set{i}_m_h.nif") }));
            }
            else if (i % 3 == 1)
            {
                variants.Add(new Variant(Gender.Female, WeightClass.Light, new[] { new Piece($"Piece{i}_F_L", (uint)(0x30000000 + i), "32 Body", $"Arma{i}_F_L", $"armor/set{i}_f_l.nif") }));
            }
            else
            {
                variants.Add(new Variant(Gender.Unisex, WeightClass.Clothing, new[] { new Piece($"Piece{i}_U_C", (uint)(0x40000000 + i), "32 Body", $"Arma{i}_U_C", $"armor/set{i}_u_c.nif") }));
            }

            sets.Add(new ArmorSet(id, name, variants));
        }

        return new Catalog(new VanillaCatalogSource("C:/Game"), sets);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T t) return t;

        // Logical tree first - works for all DependencyObjects
        if (root is FrameworkElement fe)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(fe).OfType<DependencyObject>())
            {
                var found = FindDescendant<T>(child);
                if (found is not null) return found;
            }
        }

        // Visual tree only for Visual
        if (root is Visual)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindDescendant<T>(child);
                if (found is not null) return found;
            }
        }

        return null;
    }

    private sealed class OverhaulMatrixViewModelStats
    {
        public int MatrixItemsCount { get; init; }
        public int SectionsCount { get; init; }
        public int ColumnsCount { get; init; }
    }
}
