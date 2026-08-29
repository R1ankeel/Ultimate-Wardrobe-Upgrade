using System.Windows;
using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

public partial class CellEditorWindow : Window
{
    private readonly OverhaulViewModel _viewModel;

    public CellEditorWindow(OverhaulViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseEditor();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.IsEditorOpen)
        {
            e.Cancel = true;
            _viewModel.CloseEditor();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}
