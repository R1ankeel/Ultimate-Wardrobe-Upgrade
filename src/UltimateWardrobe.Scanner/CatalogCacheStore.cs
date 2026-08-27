using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

public sealed record PluginProbe
{
    public required string Name { get; init; }

    public long Length { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }
}

/// <summary>
/// Snapshot of the source plugin set used for cache freshness: source root plus one entry per
/// plugin that a <see cref="CatalogCacheStore.BuildProbe"/> run would load, keyed by file
/// length and last-write time. A changed/stale probe means the cache must be invalidated.
/// </summary>
public sealed record CacheProbe
{
    public required string SourceRoot { get; init; }

    public required IReadOnlyList<PluginProbe> Plugins { get; init; }
}

/// <summary>
/// Canonical System.Text.Json cache for <see cref="Catalog"/> (Sprint 1.5.2). Serialization
/// uses a custom <see cref="CatalogSourceJsonConverter"/> so Core stays free of serialization
/// attributes. The cache file carries a <see cref="CacheProbe"/> so callers can test
/// freshness against the on-disk plugin set before replaying a stored catalog.
/// </summary>
public sealed class CatalogCacheStore
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static JsonSerializerOptions JsonOptions => Options;

    /// <summary>
    /// Enumerates the plugin set a scan of <paramref name="source"/> would load (same discovery
    /// rules as the pipeline) and snapshots each plugin's length and last-write time.
    /// </summary>
    public CacheProbe BuildProbe(CatalogSource source)
    {
        // Discovery needs a warning sink; probe building should be silent for absent files
        // (a missing plugin makes the probe stale, which is the desired behavior).
        var discovery = new PluginDiscovery().Discover(source, new List<ScanWarning>());

        var plugins = discovery.Plugins
            .Select(p => new PluginProbe
            {
                Name = Path.GetFileName(p.AbsolutePath),
                Length = File.Exists(p.AbsolutePath) ? new FileInfo(p.AbsolutePath).Length : 0,
                LastWriteTimeUtc = File.Exists(p.AbsolutePath) ? File.GetLastWriteTimeUtc(p.AbsolutePath) : DateTime.MinValue,
            })
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        return new CacheProbe
        {
            SourceRoot = NormalizeRoot(source.RootPath),
            Plugins = plugins,
        };
    }

    public void Save(string path, Catalog catalog, CacheProbe probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new CacheFile { FormatVersion = FormatVersion, Probe = probe, Catalog = catalog };
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, file, Options);
    }

    /// <summary>
    /// Loads a stored catalog (or null when the file is absent or unreadable). Corrupt cache
    /// files return null rather than throwing - the caller re-scans.
    /// </summary>
    public Catalog? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return TryRead(path)?.Catalog;
    }

    /// <summary>
    /// True when the cache file exists, parses, and its stored probe still matches the current
    /// on-disk plugin set for <paramref name="source"/>. Any mismatch (missing file, changed
    /// feed to length/timestamps, different plugin set) returns false.
    /// </summary>
    public bool IsFresh(string path, CatalogSource source)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var stored = TryRead(path)?.Probe;
        if (stored is null)
        {
            return false;
        }

        var fresh = BuildProbe(source);
        return ProbesEqual(stored, fresh);
    }

    private static CacheFile? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return JsonSerializer.Deserialize<CacheFile>(stream, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool ProbesEqual(CacheProbe stored, CacheProbe fresh)
    {
        if (!string.Equals(stored.SourceRoot, fresh.SourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (stored.Plugins.Count != fresh.Plugins.Count)
        {
            return false;
        }

        for (var i = 0; i < stored.Plugins.Count; i++)
        {
            var a = stored.Plugins[i];
            var b = fresh.Plugins[i];
            if (!string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                || a.Length != b.Length
                || a.LastWriteTimeUtc != b.LastWriteTimeUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeRoot(string rootPath)
    {
        return Path.GetFullPath(rootPath).TrimEnd('/', '\\');
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new CatalogSourceJsonConverter());
        return options;
    }

    private sealed class CacheFile
    {
        public int FormatVersion { get; init; }

        public CacheProbe Probe { get; init; } = null!;

        public Catalog Catalog { get; init; } = null!;
    }
}

/// <summary>
/// Serializes <see cref="CatalogSource"/> with a kind discriminator so the concrete
/// <see cref="VanillaCatalogSource"/>/<see cref="StoryModCatalogSource"/> round-trips through
/// JSON while Core carries no serialization attributes.
/// </summary>
public sealed class CatalogSourceJsonConverter : JsonConverter<CatalogSource>
{
    public override CatalogSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var kind = root.GetProperty("kind").GetString();
        var rootPath = root.GetProperty("rootPath").GetString()
            ?? throw new JsonException("Cache contains a catalog source without a rootPath.");

        switch (kind)
        {
            case "vanilla":
                return new VanillaCatalogSource(rootPath, ReadStringList(root, "pluginNames"));
            case "story":
                var mainPlugin = root.GetProperty("mainPlugin").GetString()
                    ?? throw new JsonException("Cache contains a story-mod source without a mainPlugin.");
                return new StoryModCatalogSource(rootPath, mainPlugin, ReadStringList(root, "masters"));
            default:
                throw new JsonException($"Unknown catalog source kind '{kind}'.");
        }
    }

    public override void Write(Utf8JsonWriter writer, CatalogSource value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case VanillaCatalogSource vanilla:
                writer.WriteString("kind", "vanilla");
                writer.WriteString("rootPath", vanilla.RootPath);
                writer.WritePropertyName("pluginNames");
                JsonSerializer.Serialize(writer, vanilla.PluginNames, options);
                break;
            case StoryModCatalogSource story:
                writer.WriteString("kind", "story");
                writer.WriteString("rootPath", story.RootPath);
                writer.WriteString("mainPlugin", story.MainPlugin);
                writer.WritePropertyName("masters");
                JsonSerializer.Serialize(writer, story.Masters, options);
                break;
            default:
                throw new JsonException($"Unsupported catalog source kind '{value.Kind}'.");
        }

        writer.WriteEndObject();
    }

    private static IReadOnlyList<string>? ReadStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return element.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).ToList()!;
    }
}