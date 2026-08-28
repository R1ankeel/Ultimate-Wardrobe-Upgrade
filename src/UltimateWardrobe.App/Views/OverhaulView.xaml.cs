using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Overhaul (mapping matrix) screen (Phase 6 Sprint 6.1 placeholder). The grid + popover editor
/// land in Sprint 6.4/6.5.
/// </summary>
public partial class OverhaulView : System.Windows.Controls.Page
{
    public OverhaulView(OverhaulViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
