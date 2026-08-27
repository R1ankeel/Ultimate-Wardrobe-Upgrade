using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

public sealed class LoadedMod : IDisposable
{
    public required string AbsolutePath { get; init; }

    public required ModKey ModKey { get; init; }

    public required ISkyrimModDisposableGetter Overlay { get; init; }

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

    public LoadedMod? TryLoad(string absolutePath, List<ScanWarning> warnings)
    {
        try
        {
            var overlay = SkyrimMod.CreateFromBinaryOverlay(absolutePath, _release);
            return new LoadedMod
            {
                AbsolutePath = absolutePath,
                ModKey = ModKey.FromFileName(Path.GetFileName(absolutePath)),
                Overlay = overlay,
            };
        }
        catch (Exception ex)
        {
            warnings.Add(new ScanWarning($"Plugin '{absolutePath}' could not be read and was skipped: {ex.Message}"));
            return null;
        }
    }
}