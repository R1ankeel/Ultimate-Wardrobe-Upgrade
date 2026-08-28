using System.Globalization;
using System.Text;
using UltimateWardrobe.Core.Abstractions;

namespace UltimateWardrobe.Patcher;

/// <summary>
/// Sprint 5.3.1 (plan section 4.6) - the export mod folder layout and the <c>meta.ini</c> writer.
/// The output path is <c>&lt;outputDir&gt;/UltimateWardrobe - &lt;Overhaul.Name&gt;</c> (the name
/// sanitized for Windows file names); the esp lives there under the same sanitized prefix. The full
/// folder is cleared before any write (<c>delete-then-rebuild</c>), never the output directory
/// itself or anything above it - <see cref="ResolveModDir"/> verifies the resolved path is strictly
/// under the output directory and a suspicious path surfaces as a typed <see cref="PatchException"/>
/// before anything is touched. <see cref="WriteMetaIni"/> writes the section 4.6 layout with the app
/// version and a UTC <c>generated</c> stamp that differs between runs.
/// </summary>
public static class OutputFolder
{
    public const string AppVersion = "1.0.0";

    private const string ModNamePrefix = "UltimateWardrobe - ";
    private const string MetaFileName = "meta.ini";
    private const string Category = "Armor Replacer";

    /// <summary>
    /// The export folder/plugin display name: <c>UltimateWardrobe - &lt;Overhaul.Name&gt;</c> with the
    /// name sanitized (illegal Windows file-name characters become <c>_</c>, trailing dots/spaces are
    /// trimmed). A blank name falls back to <c>Unnamed</c> so the folder always starts with the prefix.
    /// </summary>
    public static string ModName(string overhaulName)
    {
        if (string.IsNullOrWhiteSpace(overhaulName)) throw new ArgumentException("OverhaulName must not be empty.", nameof(overhaulName));

        var clean = Sanitize(overhaulName);
        return ModNamePrefix + (clean.Length > 0 ? clean : "Unnamed");
    }

    /// <summary>The output esp file name for an overhaul, derived from <see cref="ModName"/>.</summary>
    public static string PluginFileName(string overhaulName) => ModName(overhaulName) + ".esp";

    /// <summary>
    /// Resolves the mod folder for an overhaul under <paramref name="outputDir"/> as a full path and
    /// verifies it is strictly below the output directory. An output path that is a file, or a mod
    /// folder that resolves to the output directory itself or above it, throws a typed
    /// <see cref="PatchException"/> (the clean-before-write step must never delete the output
    /// directory or anything above it).
    /// </summary>
    public static string ResolveModDir(string outputDir, string overhaulName)
    {
        if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("OutputDir must not be empty.", nameof(outputDir));

        var fullOutput = Path.GetFullPath(outputDir);
        var modDir = Path.GetFullPath(Path.Combine(fullOutput, ModName(overhaulName)));

        if (File.Exists(fullOutput) || IsOnOrAbove(fullOutput, modDir))
        {
            throw new PatchException(
                $"The export mod folder '{modDir}' is the output directory '{fullOutput}' itself or above it; refusing to clear it.");
        }

        return modDir;
    }

    /// <summary>
    /// Rebuilds a mod folder empty for export: the existing folder (if any), including every stale
    /// or orphaned file from a previous export, is deleted and recreated. A folder that cannot be
    /// cleared (for example content locked by another process) surfaces as a typed
    /// <see cref="PatchException"/>.
    /// </summary>
    public static void ClearModDir(string modDir)
    {
        if (string.IsNullOrWhiteSpace(modDir)) throw new ArgumentException("ModDir must not be empty.", nameof(modDir));

        try
        {
            if (Directory.Exists(modDir))
            {
                Directory.Delete(modDir, recursive: true);
            }

            Directory.CreateDirectory(modDir);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PatchException($"Could not clear the export mod folder '{modDir}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes the section 4.6 <c>meta.ini</c> into the mod folder: display name, app version, category,
    /// a notes line carrying the overhaul name and the mapped-set count, and a UTC
    /// <c>generated</c> stamp. The stamp defaults to <see cref="DateTime.UtcNow"/> so consecutive
    /// exports differ. Returns the written file path.
    /// </summary>
    public static string WriteMetaIni(string modDir, string overhaulName, int mappedSets, DateTime? generatedUtc = null)
    {
        if (string.IsNullOrWhiteSpace(modDir)) throw new ArgumentException("ModDir must not be empty.", nameof(modDir));
        if (string.IsNullOrWhiteSpace(overhaulName)) throw new ArgumentException("OverhaulName must not be empty.", nameof(overhaulName));
        if (mappedSets < 0) throw new ArgumentOutOfRangeException(nameof(mappedSets), "MappedSets must not be negative.");

        var generated = generatedUtc ?? DateTime.UtcNow;
        var stamp = generated.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var content =
            "[General]\n" +
            $"name={ModNamePrefix}{overhaulName}\n" +
            $"version={AppVersion}\n" +
            $"category={Category}\n" +
            $"notes=Generated by UltimateWardrobe on {stamp}. Overhaul: {overhaulName}, {mappedSets} sets mapped.\n" +
            $"generated={stamp}\n";

        var path = Path.Combine(modDir, MetaFileName);
        try
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PatchException($"Could not write '{path}': {ex.Message}", ex);
        }

        return path;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return result.TrimEnd('.', ' ');
    }

    private static bool IsOnOrAbove(string outputDir, string modDir)
    {
        if (string.Equals(modDir, outputDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var modDirWithSep = modDir.EndsWith(Path.DirectorySeparatorChar) ? modDir : modDir + Path.DirectorySeparatorChar;
        return outputDir.StartsWith(modDirWithSep, StringComparison.OrdinalIgnoreCase);
    }
}