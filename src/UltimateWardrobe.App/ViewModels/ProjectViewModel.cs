using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;
using UltimateWardrobe.App.Services;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Project screen placeholder (Phase 6 Sprint 6.1): overhaul cards, progress and the donor library
/// table land in Sprint 6.2/6.3. This scaffold resolves through the composition root so the shell
/// can host the page; the commands and state are filled in by later sprints.
/// </summary>
public sealed class ProjectViewModel : ObservableObject
{
    private readonly IProjectSession _session;
    private readonly IAppNavigationService _navigation;
    private readonly IAppDialogService _dialogs;
    private readonly ILogger<ProjectViewModel> _logger;

    public ProjectViewModel(
        IProjectSession session,
        IAppNavigationService navigation,
        IAppDialogService dialogs,
        ILogger<ProjectViewModel>? logger = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? NullLogger<ProjectViewModel>.Instance;
    }

    public string ProjectName => _session.Project?.Name ?? string.Empty;
}
