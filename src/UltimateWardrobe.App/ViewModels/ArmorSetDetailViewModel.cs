using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Mapping cell editor placeholder (Phase 6 Sprint 6.1). Rescoped to a single-cell editor hosted by
/// the anchored popover (amendment 8) in Sprint 6.5; the scaffold only fixes the resolving surface.
/// </summary>
public sealed class ArmorSetDetailViewModel : ObservableObject
{
    private readonly IBackgroundTaskService _backgroundTasks;
    private readonly IAppDialogService _dialogs;
    private readonly ILogger<ArmorSetDetailViewModel> _logger;

    public ArmorSetDetailViewModel(
        IBackgroundTaskService backgroundTasks,
        IAppDialogService dialogs,
        ILogger<ArmorSetDetailViewModel>? logger = null)
    {
        _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? NullLogger<ArmorSetDetailViewModel>.Instance;
    }
}
