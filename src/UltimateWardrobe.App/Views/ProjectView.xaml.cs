using System.Windows.Controls;
using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Project screen (Phase 6 Sprint 6.1 placeholder). The overhaul cards + donor library table land
/// in Sprint 6.2/6.3.
/// </summary>
public partial class ProjectView : System.Windows.Controls.Page
{
    public ProjectView(ProjectViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
