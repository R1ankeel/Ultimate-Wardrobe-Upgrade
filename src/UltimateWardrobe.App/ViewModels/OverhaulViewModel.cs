using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.App.Infrastructure;

namespace UltimateWardrobe.App.ViewModels;

/// <summary>
/// Overhaul (mapping matrix) screen placeholder (Phase 6 Sprint 6.1). The full per-Overhaul
/// FEMALE/MALE ARMOR matrix grid with the anchored-popover cell editor (amendment 8) lands in
/// Sprint 6.4/6.5; this scaffold only proves the page resolves through the composition root.
/// </summary>
public sealed class OverhaulViewModel : ObservableObject
{
    private readonly IAppNavigationService _navigation;
    private readonly IAppDialogService _dialogs;
    private readonly ILogger<OverhaulViewModel> _logger;

    public OverhaulViewModel(
        IAppNavigationService navigation,
        IAppDialogService dialogs,
        ILogger<OverhaulViewModel>? logger = null)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? NullLogger<OverhaulViewModel>.Instance;
    }
}
