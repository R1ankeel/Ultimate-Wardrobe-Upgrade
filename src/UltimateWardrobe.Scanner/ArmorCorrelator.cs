using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Pre-Piece data produced by the ARMO -> ARMA -> files correlation stage. Carries the
/// mesh/texture paths and the raw extraction needed by later stages (grouping, gender/weight
/// split), independent of the final <see cref="Piece"/> shape.
/// </summary>
public sealed class CorrelatedArmor
{
    public required string EditorId { get; init; }

    public required uint FormId { get; init; }

    public required IArmorGetter Armor { get; init; }

    public IArmorAddonGetter? FirstAddon { get; init; }

    public string? ArmaEditorId { get; init; }

    public string? MeshPath { get; init; }

    public IReadOnlyList<string> TexturePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> WeaponKeywordIds { get; init; } = Array.Empty<string>();

    public BipedObjectFlag BipedFlags { get; init; }

    public FormLinkNullable<IRaceGetter> RaceLink { get; init; } = new();
}

/// <summary>
/// Correlates raw ARMO/ARMA records into <see cref="CorrelatedArmor"/>: resolves the first
/// armature link to an ARMA, derives a logical mesh path, and resolves skin-texture TXST
/// records into a deduplicated, ordinal-sorted texture list. Unresolvable links or missing
/// masters raise a <see cref="ScanWarning"/> attributed to the affected ARMO only and never
/// abort the scan.
/// </summary>
public sealed class ArmorCorrelator
{
    private static readonly IReadOnlyList<string> TextureFieldNames =
        new[] { "Diffuse", "NormalOrGloss", "EnvironmentMaskOrSubsurfaceTint", "GlowOrDetailMap" };

    private readonly FileResolver? _resolver;

    public ArmorCorrelator(FileResolver? resolver = null)
    {
        _resolver = resolver;
    }

    public IReadOnlyList<CorrelatedArmor> Correlate(RecordIndex index, List<ScanWarning> warnings)
    {
        var result = new List<CorrelatedArmor>();

        foreach (var armor in index.EnumerateArmor())
        {
            result.Add(CorrelateOne(armor, index, warnings));
        }

        return result;
    }

    public CorrelatedArmor CorrelateOne(IArmorGetter armor, RecordIndex index, List<ScanWarning> warnings)
    {
        var editorId = armor.EditorID ?? $"FormId:{armor.FormKey.IDString()}";
        var keywordIds = ExtractKeywordIds(armor);

        IArmorAddonGetter? firstAddon = null;
        string? armaEditorId = null;
        string? meshPath = null;
        var raceLink = new FormLinkNullable<IRaceGetter>();

        var armature = armor.Armature;
        if (armature is not null)
        {
            foreach (var link in armature)
            {
                if (link.IsNull || link.FormKey.IsNull)
                {
                    continue;
                }

                if (index.TryResolveArmorAddon(link.FormKey, out var addon))
                {
                    firstAddon = addon;
                    armaEditorId = addon.EditorID;
                    raceLink = new FormLinkNullable<IRaceGetter>(addon.Race.FormKey);
                    meshPath = ResolveMeshPath(addon);
                    break;
                }

                warnings.Add(new ScanWarning(
                    $"Armor '{editorId}' (FormId {armor.FormKey.IDString()}) references armature '{link.FormKey}' that " +
                    "could not be resolved in the loaded file set; proceeding without its ARMA.",
                    editorId));
            }
        }

        var texturePaths = ResolveTexturePaths(armor, editorId, firstAddon, index, warnings);

        return new CorrelatedArmor
        {
            EditorId = editorId,
            FormId = armor.FormKey.ID,
            Armor = armor,
            FirstAddon = firstAddon,
            ArmaEditorId = armaEditorId,
            MeshPath = meshPath,
            TexturePaths = texturePaths,
            WeaponKeywordIds = keywordIds,
            BipedFlags = armor.BodyTemplate?.FirstPersonFlags ?? (BipedObjectFlag)0,
            RaceLink = raceLink,
        };
    }

    private static IReadOnlyList<string> ExtractKeywordIds(IArmorGetter armor)
    {
        if (armor.Keywords is null)
        {
            return Array.Empty<string>();
        }

        var ids = new List<string>(armor.Keywords.Count);
        foreach (var k in armor.Keywords)
        {
            if (!k.IsNull)
            {
                ids.Add(k.FormKey.ToString());
            }
        }

        return ids;
    }

    private static string? ResolveMeshPath(IArmorAddonGetter addon)
    {
        return FirstNonNullPath(addon.WorldModel?.Male?.File, addon.WorldModel?.Female?.File);
    }

    private static string? FirstNonNullPath(params AssetLinkGetter<SkyrimModelAssetType>?[] links)
    {
        foreach (var link in links)
        {
            if (link is not null && !link.IsNull && !string.IsNullOrWhiteSpace(link.GivenPath))
            {
                return link.GivenPath.Replace('\\', '/');
            }
        }

        return null;
    }

    private IReadOnlyList<string> ResolveTexturePaths(
        IArmorGetter armor,
        string editorId,
        IArmorAddonGetter? addon,
        RecordIndex index,
        List<ScanWarning> warnings)
    {
        if (addon?.SkinTexture is null)
        {
            return Array.Empty<string>();
        }

        var maleSet = addon.SkinTexture.Male;
        var femaleSet = addon.SkinTexture.Female;

        var textureSets = new HashSet<ITextureSetGetter>();
        foreach (var link in new[] { maleSet, femaleSet })
        {
            if (link is null || link.FormKey.IsNull || !index.TryResolveTextureSet(link.FormKey, out var textureSet))
            {
                continue;
            }

            textureSets.Add(textureSet);
        }

        if (textureSets.Count == 0)
        {
            if ((maleSet is not null && !maleSet.FormKey.IsNull)
                || (femaleSet is not null && !femaleSet.FormKey.IsNull))
            {
                warnings.Add(new ScanWarning(
                    $"Armor '{editorId}' (FormId {armor.FormKey.IDString()}) references a skin texture set that could not be " +
                    "resolved in the loaded file set; proceeding without textures.",
                    editorId));
            }

            return Array.Empty<string>();
        }

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var textureSet in textureSets)
        {
            foreach (var name in TextureFieldNames)
            {
                var link = TextureField(textureSet, name);
                if (link is not null && !link.IsNull && !string.IsNullOrWhiteSpace(link.GivenPath))
                {
                    paths.Add(link.GivenPath.Replace('\\', '/'));
                }
            }
        }

        return paths.ToList();
    }

    private static AssetLinkGetter<SkyrimTextureAssetType>? TextureField(ITextureSetGetter textureSet, string name)
    {
        return name switch
        {
            "Diffuse" => textureSet.Diffuse,
            "NormalOrGloss" => textureSet.NormalOrGloss,
            "EnvironmentMaskOrSubsurfaceTint" => textureSet.EnvironmentMaskOrSubsurfaceTint,
            "GlowOrDetailMap" => textureSet.GlowOrDetailMap,
            _ => null,
        };
    }
}
