using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

public sealed class LoadedMod : IDisposable
{
    public required string AbsolutePath { get; init; }

    public required ModKey ModKey { get; init; }

    public required ISkyrimModDisposableGetter Overlay { get; init; }

    /// <summary>
    /// True when the mod is linked for master/FormLink resolution only and its ARMO/ARMA
    /// records must not become catalog content (Sprint 6.9 vanilla <c>Update.esm</c>).
    /// </summary>
    public bool IsResolutionOnly { get; init; }

    public void Dispose() => Overlay.Dispose();
}

public sealed class ModLoader
{
    private readonly SkyrimRelease _release;

    public ModLoader(SkyrimRelease release = SkyrimRelease.SkyrimSE)
    {
        _release = release;
    }

    public IReadOnlyList<ModKey> ReadMasters(string absolutePath)
    {
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(absolutePath, _release);
        return overlay.MasterReferences.Select(m => m.Master).ToList();
    }

    public LoadedMod? TryLoad(DiscoveredPlugin plugin, List<ScanWarning> warnings)
    {
        return TryLoad(plugin.AbsolutePath, plugin.IsResolutionOnly, warnings);
    }

    public LoadedMod? TryLoad(string absolutePath, List<ScanWarning> warnings)
    {
        return TryLoad(absolutePath, isResolutionOnly: false, warnings);
    }

    public LoadedMod? TryLoad(string absolutePath, bool isResolutionOnly, List<ScanWarning> warnings)
    {
        try
        {
            var overlay = SkyrimMod.CreateFromBinaryOverlay(absolutePath, _release);
            return new LoadedMod
            {
                AbsolutePath = absolutePath,
                ModKey = ModKey.FromFileName(Path.GetFileName(absolutePath)),
                Overlay = overlay,
                IsResolutionOnly = isResolutionOnly,
            };
        }
        catch (Exception ex)
        {
            warnings.Add(new ScanWarning($"Plugin '{absolutePath}' could not be read and was skipped: {ex.Message}"));
            return null;
        }
    }
}