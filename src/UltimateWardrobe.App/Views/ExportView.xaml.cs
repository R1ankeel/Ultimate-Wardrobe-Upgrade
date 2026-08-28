using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Export screen (Phase 6 Sprint 6.6). Thin code-behind: sets the <see cref="ExportViewModel"/>
/// data context and refreshes the checklist each time the page is shown.
/// </summary>
public partial class ExportView : System.Windows.Controls.Page
{
    private readonly ExportViewModel _viewModel;

    public ExportView(ExportViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => _viewModel.Refresh();
    }
}
