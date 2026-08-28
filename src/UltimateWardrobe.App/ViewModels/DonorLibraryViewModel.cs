using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Donor library screen placeholder (Phase 6 Sprint 6.1). The import drop zone + table land in
/// Sprint 6.3; the scaffold only proves the page resolves through the composition root.
/// </summary>
public sealed class DonorLibraryViewModel : ObservableObject
{
    private readonly IAppNavigationService _navigation;
    private readonly IAppDialogService _dialogs;
    private readonly ILogger<DonorLibraryViewModel> _logger;

    public DonorLibraryViewModel(
        IAppNavigationService navigation,
        IAppDialogService dialogs,
        ILogger<DonorLibraryViewModel>? logger = null)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? NullLogger<DonorLibraryViewModel>.Instance;
    }
}
