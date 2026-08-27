using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Core.Abstractions;

public sealed class PatchResult
{
    public string PluginPath { get; init; }
    public IReadOnlyList<string> CopiedFiles { get; init; }

    public PatchResult(string pluginPath, IReadOnlyList<string> copiedFiles)
    {
        if (string.IsNullOrWhiteSpace(pluginPath)) throw new ArgumentException("PluginPath must not be empty.", nameof(pluginPath));
        PluginPath = pluginPath;
        CopiedFiles = copiedFiles ?? throw new ArgumentNullException(nameof(copiedFiles));
    }
}

public interface IPatcher
{
    Task<PatchResult> BuildAsync(Overhaul overhaul, string outputDir, CancellationToken cancellationToken = default);
}
