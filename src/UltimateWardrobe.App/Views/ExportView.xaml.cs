using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Export screen (Phase 6 Sprint 6.1 placeholder). The checklist + "build wardrobe" invocation of
/// <c>IPatcher</c> land in Sprint 6.6.
/// </summary>
public partial class ExportView : System.Windows.Controls.Page
{
    public ExportView(ExportViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
