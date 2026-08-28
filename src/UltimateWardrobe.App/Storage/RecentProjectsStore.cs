using System.IO;
using System.Text.Json;

namespace UltimateWardrobe.App.Storage;

/// <summary>
/// Persists the most recent project database paths under
/// <c>%LocalAppData%\UltimateWardrobe\settings.json</c> (Phase 6 Sprint 6.1). Deduplicates by full
/// path (ordinal, case-insensitive as on Windows), keeps the newest first, caps at 8 entries and
/// degrades to an empty list when the file is corrupt or unreadable. The settings path is
/// constructor-injectable so tests can run against a temp file.
/// </summary>
public sealed class RecentProjectsStore
{
    private const int MaxEntries = 8;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;

    public RecentProjectsStore()
        : this(DefaultSettingsPath())
    {
    }

    public RecentProjectsStore(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("Settings path must not be empty.", nameof(settingsPath));
        }
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public IReadOnlyList<string> GetRecentProjectPaths()
    {
        if (!File.Exists(_settingsPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<RecentSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            return settings?.RecentProjects ?? new List<string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public void AddRecentProject(string projectDbPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectDbPath);

        var normalized = Path.GetFullPath(projectDbPath);
        var entries = GetRecentProjectPaths()
            .Where(existing => !string.Equals(Path.GetFullPath(existing), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        entries.Insert(0, normalized);
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            _settingsPath,
            JsonSerializer.Serialize(new RecentSettings { RecentProjects = entries }, JsonOptions));
    }

    private static string DefaultSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UltimateWardrobe",
            "settings.json");
    }

    private sealed class RecentSettings
    {
        public List<string> RecentProjects { get; set; } = new();
    }
}