using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Persistence;

/// <summary>
/// Serializes <see cref="CatalogSource"/> with a kind discriminator so the concrete
/// <see cref="VanillaCatalogSource"/> / <see cref="StoryModCatalogSource"/> round-trips through
/// JSON while Core carries no serialization attributes. Semantically identical to the Scanner's
/// <c>CatalogSourceJsonConverter</c>, but lives in Persistence (which may not reference Scanner).
/// Phase 4 plan section 4.5.
/// </summary>
public sealed class CatalogSourceJsonConverter : JsonConverter<CatalogSource>
{
    public override CatalogSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var kind = root.GetProperty("kind").GetString();
        var rootPath = root.GetProperty("rootPath").GetString()
            ?? throw new JsonException("Catalog source without a rootPath.");

        switch (kind)
        {
            case "vanilla":
                return new VanillaCatalogSource(rootPath, ReadStringList(root, "pluginNames"));
            case "story":
                var mainPlugin = root.GetProperty("mainPlugin").GetString()
                    ?? throw new JsonException("Story-mod source without a mainPlugin.");
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
