using System.IO;

namespace UltimateWardrobe.App.Services;

/// <summary>
/// Validates that the game/mod roots referenced by an <see cref="UltimateWardrobe.Core.Domain.Overhaul"/>
/// source actually exist (Phase 6 Sprint 6.1). Pure filesystem checks so it can be exercised
/// headless and surfaced to the user before a scan.
/// </summary>
public interface IOverhaulSourceValidator
{
    IReadOnlyList<string> ValidateVanilla(string gameRootPath);

    IReadOnlyList<string> ValidateStoryMod(string gameRootPath, string mainPlugin, string modRootPath, IReadOnlyList<string>? masters = null);
}

/// <summary>
/// Default <see cref="IOverhaulSourceValidator"/>: Vanilla maps require the game root with
/// <c>Data\Skyrim.esm</c>; a story mod requires its main plugin under <c>Data</c> and every declared
/// master to exist in <c>Data</c> or the mod root (Phase 6 amendment 3 - 4.4 session wording).
/// </summary>
public sealed class OverhaulSourceValidator : IOverhaulSourceValidator
{
    private const string DataFolder = "Data";
    private const string SkyrimEsm = "Skyrim.esm";

    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    public IReadOnlyList<string> ValidateVanilla(string gameRootPath)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath))
        {
            errors.Add("Game root path does not exist.");
            return errors;
        }

        if (!File.Exists(Path.Combine(gameRootPath, DataFolder, SkyrimEsm)))
        {
            errors.Add($"Game root is missing {Path.Combine(DataFolder, SkyrimEsm)}.");
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateStoryMod(string gameRootPath, string mainPlugin, string modRootPath, IReadOnlyList<string>? masters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainPlugin);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath))
        {
            errors.Add("Game root path does not exist.");
        }
        else if (!File.Exists(Path.Combine(gameRootPath, DataFolder, SkyrimEsm)))
        {
            errors.Add($"Game root is missing {Path.Combine(DataFolder, SkyrimEsm)}.");
        }

        if (string.IsNullOrWhiteSpace(modRootPath) || !Directory.Exists(modRootPath))
        {
            errors.Add("Story mod root path does not exist.");
        }

        var pluginPath = LocatePlugin(gameRootPath, modRootPath, mainPlugin);
        if (pluginPath is null)
        {
            errors.Add($"Story mod main plugin '{mainPlugin}' was not found under {DataFolder} or the mod root.");
        }
        else if (masters is not null)
        {
            var pluginDir = Path.GetDirectoryName(pluginPath);
            foreach (var master in masters)
            {
                if (string.IsNullOrWhiteSpace(master))
                {
                    continue;
                }

                if (pluginDir is null || !File.Exists(Path.Combine(pluginDir, master)))
                {
                    errors.Add($"Story mod master '{master}' was not found beside the main plugin.");
                }
            }
        }

        return errors;
    }

    private static string? LocatePlugin(string gameRootPath, string modRootPath, string mainPlugin)
    {
        var inData = File.Exists(Path.Combine(gameRootPath, DataFolder, mainPlugin))
            ? Path.GetFullPath(Path.Combine(gameRootPath, DataFolder, mainPlugin))
            : null;
        if (inData is not null)
        {
            return inData;
        }

        var inMod = File.Exists(Path.Combine(modRootPath, mainPlugin))
            ? Path.GetFullPath(Path.Combine(modRootPath, mainPlugin))
            : null;
        if (inMod is not null)
        {
            return inMod;
        }

        var inModData = File.Exists(Path.Combine(modRootPath, DataFolder, mainPlugin))
            ? Path.GetFullPath(Path.Combine(modRootPath, DataFolder, mainPlugin))
            : null;
        return inModData;
    }
}