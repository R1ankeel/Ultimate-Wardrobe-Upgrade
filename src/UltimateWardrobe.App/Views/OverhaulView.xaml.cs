using System.Windows;
using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Overhaul (mapping matrix) screen (Phase 6 Sprint 6.4, amendment 8): renders the FEMALE/MALE ARMOR
/// matrix - gender sections with catalog-set row bands and one column per weight class, mapped cells as
/// cards, unmapped/missing-variant cells blank. Cell clicks activate the VM (popover anchor, Sprint 6.5).
/// Rebuilt from DI per navigation, so it refreshes the matrix on Loaded.
/// </summary>
public partial class OverhaulView : System.Windows.Controls.Page
{
    public OverhaulView(OverhaulViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.Refresh();
    }
}
