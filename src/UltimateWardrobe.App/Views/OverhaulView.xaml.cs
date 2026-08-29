using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Overhaul (mapping matrix) screen (Phase 6 Sprint 6.4, amendment 8): renders the FEMALE/MALE ARMOR
/// matrix - gender sections with catalog-set row bands and one column per weight class, mapped cells as
/// cards, unmapped/missing-variant cells blank. Cell clicks activate the VM and show the CellEditorWindow
/// (UI Fix 3.3 - Window instead of Popup, draggable/sizable 1.5x). Rebuilt from DI per navigation, so it refreshes the matrix on Loaded.
/// </summary>
public partial class OverhaulView : System.Windows.Controls.Page
{
    private CellEditorWindow? _editorWindow;
    private OverhaulViewModel? _vm;

    public OverhaulView(OverhaulViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        DataContextChanged += OnDataContextChanged;
        AttachViewModel(viewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is OverhaulViewModel vm)
        {
            vm.Refresh();
        }

        Dispatcher.BeginInvoke(() => MatrixListView.Focus(), DispatcherPriority.Loaded);
    }

    private void OnListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        var sv = FindVisualChild<ScrollViewer>(MatrixListView);
        if (sv != null)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3);
            e.Handled = true;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is OverhaulViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is OverhaulViewModel newVm)
        {
            AttachViewModel(newVm);
        }
    }

    private void AttachViewModel(OverhaulViewModel vm)
    {
        _vm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverhaulViewModel.IsEditorOpen) && _vm is not null)
        {
            if (_vm.IsEditorOpen)
            {
                ShowEditor();
            }
            else
            {
                HideEditor();
            }
        }
    }

    private void ShowEditor()
    {
        if (_vm is null) return;
        if (_editorWindow is null)
        {
            _editorWindow = new CellEditorWindow(_vm);
            _editorWindow.Closed += (_, _) => _editorWindow = null;
        }
        if (!_editorWindow.IsVisible)
        {
            _editorWindow.Owner = Window.GetWindow(this);
            _editorWindow.Show();
        }
        _editorWindow.Activate();
    }

    private void HideEditor()
    {
        if (_editorWindow is not null && _editorWindow.IsVisible)
        {
            _editorWindow.Hide();
        }
    }

    private void MatrixCell_Click(object sender, RoutedEventArgs e)
    {
        // Placement no longer needed - Window is centered on Owner; keep handler to avoid XAML break but do nothing.
    }

    private void MatrixRow_Click(object sender, RoutedEventArgs e)
    {
    }

    /// <summary>Closes the editor on Esc (Sprint 6.5, kept for Window).</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is OverhaulViewModel vm && vm.IsEditorOpen)
        {
            vm.CloseEditor();
            e.Handled = true;
        }
    }
}
