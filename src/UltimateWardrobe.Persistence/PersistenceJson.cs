using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimateWardrobe.Persistence;

/// <summary>
/// The shared System.Text.Json settings for every persistence JSON column / cache, reusing the
/// proven <c>Scanner.CatalogCacheStore</c> conventions: camelCase with a
/// <see cref="JsonStringEnumConverter"/> and <c>WhenWritingNull</c>. Persistence is kept
/// dependency-free (Core only) so its own <see cref="CatalogSourceJsonConverter"/> is registered
/// here rather than referencing the Scanner one (Phase 4 plan section 4.5).
/// </summary>
public static class PersistenceJson
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    /// <summary>The options used for every persistence-serialized value. Reuse, never mutate.</summary>
    public static JsonSerializerOptions JsonOptions => Options;

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

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
}
