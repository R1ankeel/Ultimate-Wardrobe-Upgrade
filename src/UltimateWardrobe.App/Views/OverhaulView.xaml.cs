using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Overhaul (mapping matrix) screen (Phase 6 Sprint 6.4, amendment 8): renders the FEMALE/MALE ARMOR
/// matrix - gender sections with catalog-set row bands and one column per weight class, mapped cells as
/// cards, unmapped/missing-variant cells blank. Cell clicks activate the VM and anchor the single-cell
/// editor popover (Sprint 6.5). Rebuilt from DI per navigation, so it refreshes the matrix on Loaded.
/// </summary>
public partial class OverhaulView : System.Windows.Controls.Page
{
    public OverhaulView(OverhaulViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.Refresh();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Anchors the popover to the clicked cell's bounds (Sprint 6.5).</summary>
    private void MatrixCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MatrixCellViewModel cell })
        {
            CellEditorPopup.PlacementTarget = (Button)sender;
        }
    }

    /// <summary>Closes the anchored editor on Esc (Sprint 6.5).</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is OverhaulViewModel vm && vm.IsEditorOpen)
        {
            vm.CloseEditor();
            e.Handled = true;
        }
    }
}
