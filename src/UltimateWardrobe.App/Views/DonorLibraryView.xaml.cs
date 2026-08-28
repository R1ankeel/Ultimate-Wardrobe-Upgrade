using System.IO;
using System.Windows;
using UltimateWardrobe.App.ViewModels;

namespace UltimateWardrobe.App.Views;

/// <summary>
/// Donor library screen (Phase 6 Sprint 6.3): the import drop zone + the donor table. Drop handling
/// lives in <see cref="OnDrop"/> - it extracts the dropped file paths (walking explorer folder drops)
/// and hands them to <see cref="DonorLibraryViewModel.ImportCommand"/>; the view model filters to
/// supported archive extensions and runs the import. Columns/cards refresh on every load.
/// </summary>
public partial class DonorLibraryView : System.Windows.Controls.Page
{
    private readonly DonorLibraryViewModel _viewModel;

    public DonorLibraryView(DonorLibraryViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        Loaded += (_, _) => _viewModel.Refresh();
        InitializeComponent();
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            var files = FlattenFolders(dropped);
            if (files.Count > 0)
            {
                await _viewModel.ImportCommand.ExecuteAsync(files);
            }
        }
    }

    private static List<string> FlattenFolders(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                result.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories));
            }
            else if (File.Exists(path))
            {
                result.Add(path);
            }
        }

        return result;
    }
}
