using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

public abstract class CatalogSource
{
    public string RootPath { get; }
    public CatalogSourceKind Kind { get; }

    protected CatalogSource(string rootPath, CatalogSourceKind kind)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("RootPath must not be empty.", nameof(rootPath));
        if (kind == CatalogSourceKind.Unknown) throw new ArgumentException("Kind must not be Unknown.", nameof(kind));
        RootPath = rootPath;
        Kind = kind;
    }
}

public sealed class VanillaCatalogSource : CatalogSource
{
    public IReadOnlyList<string> PluginNames { get; }

    public VanillaCatalogSource(string rootPath, IReadOnlyList<string>? pluginNames = null)
        : base(rootPath, CatalogSourceKind.VanillaPlusDlc)
    {
        PluginNames = pluginNames ?? Array.Empty<string>();
    }
}

public sealed class StoryModCatalogSource : CatalogSource
{
    public string MainPlugin { get; }
    public IReadOnlyList<string> Masters { get; }

    public StoryModCatalogSource(string rootPath, string mainPlugin, IReadOnlyList<string>? masters = null)
        : base(rootPath, CatalogSourceKind.StoryMod)
    {
        if (string.IsNullOrWhiteSpace(mainPlugin)) throw new ArgumentException("MainPlugin must not be empty.", nameof(mainPlugin));
        MainPlugin = mainPlugin;
        Masters = masters ?? Array.Empty<string>();
    }
}
