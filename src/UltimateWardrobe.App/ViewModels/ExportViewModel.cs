using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Export screen placeholder (Phase 6 Sprint 6.1). The checklist + "build wardrobe" invocation of
/// <c>IPatcher</c> with progress and the result card land in Sprint 6.6; the scaffold only proves
/// the page resolves through the composition root.
/// </summary>
public sealed class ExportViewModel : ObservableObject
{
    private readonly IBackgroundTaskService _backgroundTasks;
    private readonly IAppDialogService _dialogs;
    private readonly ILogger<ExportViewModel> _logger;

    public ExportViewModel(
        IBackgroundTaskService backgroundTasks,
        IAppDialogService dialogs,
        ILogger<ExportViewModel>? logger = null)
    {
        _backgroundTasks = backgroundTasks ?? throw new ArgumentNullException(nameof(backgroundTasks));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? NullLogger<ExportViewModel>.Instance;
    }
}
