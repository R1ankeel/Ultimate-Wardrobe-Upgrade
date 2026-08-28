using System.Windows.Controls;
using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Project screen (Phase 6 Sprint 6.2): the overhaul cards (add/rename/delete/select) over the
/// open project's graph. Cards are refreshed every time the page is (re)loaded.
/// </summary>
public partial class ProjectView : System.Windows.Controls.Page
{
    private readonly ProjectViewModel _viewModel;

    public ProjectView(ProjectViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        Loaded += (_, _) => _viewModel.Refresh();
        InitializeComponent();
    }
}
