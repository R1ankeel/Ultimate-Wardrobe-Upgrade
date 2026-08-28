using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Core.Abstractions;

/// <summary>
/// A non-fatal problem encountered while building an export (Sprint 5.0.2 Core amendment).
/// Per-mapping issues skip only that mapping (the build continues); build-blocking failures
/// surface as typed exceptions, never as warnings.
/// </summary>
public sealed class PatchWarning
{
    public string Message { get; init; }
    public string? Context { get; init; }

    public PatchWarning(string message, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message must not be empty.", nameof(message));
        Message = message;
        Context = context;
    }
}

/// <summary>
/// The progress payload for <c>IProgress&lt;PatchProgress&gt;</c> (Sprint 5.0.2 Core amendment,
/// roadmap 10.5 build progress): a coarse pipeline stage with a completed/total pair; <see cref="Detail"/>
/// carries an optional human-readable hint (e.g. a per-mapping or per-file line).
/// </summary>
public sealed class PatchProgress
{
    public string Stage { get; init; }
    public int Completed { get; init; }
    public int Total { get; init; }
    public string? Detail { get; init; }

    public PatchProgress(string stage, int completed = 0, int total = 0, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(stage)) throw new ArgumentException("Stage must not be empty.", nameof(stage));
        if (completed < 0) throw new ArgumentOutOfRangeException(nameof(completed), "Completed must not be negative.");
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), "Total must not be negative.");
        Stage = stage;
        Completed = completed;
        Total = total;
        Detail = detail;
    }
}

/// <summary>
/// The full Phase 5 build report (Sprint 5.0.2 Core amendment). Wired by the patcher over
/// Sprints 5.1-5.3: <see cref="ResolvedMappings"/>/<see cref="SkippedMappings"/> from resolution,
/// <see cref="OverriddenRecords"/> from the plugin writer, <see cref="CopiedFiles"/>/<see cref="CopiedBytes"/>
/// from the file slicer. Self-contained so the Phase 6 UI can render it without the <see cref="PatchResult"/>.
/// </summary>
public sealed class PatchReport
{
    public int TotalMappings { get; init; }
    public int ResolvedMappings { get; init; }
    public int SkippedMappings { get; init; }
    public int OverriddenRecords { get; init; }
    public IReadOnlyList<string> CopiedFiles { get; init; } = Array.Empty<string>();
    public long CopiedBytes { get; init; }
    public IReadOnlyList<PatchWarning> Warnings { get; init; } = Array.Empty<PatchWarning>();
}

public sealed class PatchResult
{
    public string PluginPath { get; init; }
    public IReadOnlyList<string> CopiedFiles { get; init; }

    /// <summary>
    /// The full build report (Sprint 5.0.2 Core amendment, additive). <c>null</c> until the
    /// orchestrator attaches one; <see cref="PatchReport.CopiedFiles"/> mirrors this instance's
    /// <see cref="CopiedFiles"/> once the report exists. Existing constructor callers are unaffected.
    /// </summary>
    public PatchReport? Report { get; init; }

    public PatchResult(string pluginPath, IReadOnlyList<string> copiedFiles)
    {
        if (string.IsNullOrWhiteSpace(pluginPath)) throw new ArgumentException("PluginPath must not be empty.", nameof(pluginPath));
        PluginPath = pluginPath;
        CopiedFiles = copiedFiles ?? throw new ArgumentNullException(nameof(copiedFiles));
    }
}

public interface IPatcher
{
    Task<PatchResult> BuildAsync(
        Overhaul overhaul,
        DonorLibrary donorLibrary,
        string outputDir,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
