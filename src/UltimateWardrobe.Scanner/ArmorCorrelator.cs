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

    public string? ArmaEditorIdMale { get; init; }

    public string? ArmaEditorIdFemale { get; init; }

    public string? MeshPath { get; init; }

    public string? MeshPathMale { get; init; }

    public string? MeshPathFemale { get; init; }

    public IReadOnlyList<IArmorAddonGetter> AllAddons { get; init; } = Array.Empty<IArmorAddonGetter>();

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

    public IReadOnlyList<CorrelatedArmor> Correlate(
        RecordIndex index,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CorrelatedArmor>();

        foreach (var armor in index.EnumerateArmor())
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(ScanReportBuilder.Guard("correlating armor", armor.EditorID, () => CorrelateOne(armor, index, warnings)));
        }

        return result;
    }

    public CorrelatedArmor CorrelateOne(IArmorGetter armor, RecordIndex index, List<ScanWarning> warnings)
    {
        var editorId = armor.EditorID ?? $"FormId:{armor.FormKey.IDString()}";
        var keywordIds = ExtractKeywordIds(armor);

        IArmorAddonGetter? firstAddon = null;
        string? armaEditorId = null;
        string? armaEditorIdMale = null;
        string? armaEditorIdFemale = null;
        string? meshPath = null;
        string? meshPathMale = null;
        string? meshPathFemale = null;
        var raceLink = new FormLinkNullable<IRaceGetter>();
        var allAddons = new List<IArmorAddonGetter>();

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
                    allAddons.Add(addon);
                    if (firstAddon is null)
                    {
                        firstAddon = addon;
                        armaEditorId = addon.EditorID;
                        raceLink = new FormLinkNullable<IRaceGetter>(addon.Race.FormKey);
                        meshPath = ResolveMeshPath(addon);
                    }

                    // Per-gender mesh and ARMA tracking (F2 fix)
                    var maleFile = addon.WorldModel?.Male?.File;
                    if (meshPathMale is null && maleFile is not null && !maleFile.IsNull && !string.IsNullOrWhiteSpace(maleFile.GivenPath))
                    {
                        meshPathMale = maleFile.GivenPath.Replace('\\', '/');
                        armaEditorIdMale ??= addon.EditorID;
                    }

                    var femaleFile = addon.WorldModel?.Female?.File;
                    if (meshPathFemale is null && femaleFile is not null && !femaleFile.IsNull && !string.IsNullOrWhiteSpace(femaleFile.GivenPath))
                    {
                        meshPathFemale = femaleFile.GivenPath.Replace('\\', '/');
                        armaEditorIdFemale ??= addon.EditorID;
                    }

                    // If addon has no explicit model for a gender but slider signals that gender, keep ARMA id as fallback
                    if (armaEditorIdMale is null && addon.WeightSliderEnabled?.Male == true)
                    {
                        armaEditorIdMale = addon.EditorID;
                    }

                    if (armaEditorIdFemale is null && addon.WeightSliderEnabled?.Female == true)
                    {
                        armaEditorIdFemale = addon.EditorID;
                    }

                    continue;
                }

                warnings.Add(new ScanWarning(
                    $"Armor '{editorId}' (FormId {armor.FormKey.IDString()}) references armature '{link.FormKey}' that " +
                    "could not be resolved in the loaded file set; proceeding without its ARMA.",
                    editorId));
            }
        }

        // Fallback per-gender meshes from first addon if still null (already handled above, but keep for edge)
        if (meshPathMale is null && firstAddon?.WorldModel?.Male?.File is { } mf2 && !mf2.IsNull && !string.IsNullOrWhiteSpace(mf2.GivenPath))
        {
            meshPathMale = mf2.GivenPath.Replace('\\', '/');
            armaEditorIdMale ??= firstAddon.EditorID;
        }

        if (meshPathFemale is null && firstAddon?.WorldModel?.Female?.File is { } ff2 && !ff2.IsNull && !string.IsNullOrWhiteSpace(ff2.GivenPath))
        {
            meshPathFemale = ff2.GivenPath.Replace('\\', '/');
            armaEditorIdFemale ??= firstAddon.EditorID;
        }

        // Ensure per-gender ARMA ids fall back to firstAddon if not set
        armaEditorIdMale ??= armaEditorId;
        armaEditorIdFemale ??= armaEditorId;

        // Backward-compat MeshPath already set to first addon's first non-null (male preferred)
        // If still null but we have per-gender meshes, use male as fallback
        meshPath ??= meshPathMale ?? meshPathFemale;

        var texturePaths = ResolveTexturePaths(armor, editorId, allAddons, firstAddon, index, warnings);

        return new CorrelatedArmor
        {
            EditorId = editorId,
            FormId = armor.FormKey.ID,
            Armor = armor,
            FirstAddon = firstAddon,
            ArmaEditorId = armaEditorId,
            ArmaEditorIdMale = armaEditorIdMale,
            ArmaEditorIdFemale = armaEditorIdFemale,
            MeshPath = meshPath,
            MeshPathMale = meshPathMale,
            MeshPathFemale = meshPathFemale,
            AllAddons = allAddons,
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
        IReadOnlyList<IArmorAddonGetter> allAddons,
        IArmorAddonGetter? firstAddon,
        RecordIndex index,
        List<ScanWarning> warnings)
    {
        var addonsToCheck = allAddons.Count > 0 ? allAddons : (firstAddon is not null ? new[] { firstAddon } : Array.Empty<IArmorAddonGetter>());

        if (addonsToCheck.Count == 0)
        {
            return Array.Empty<string>();
        }

        var textureSets = new HashSet<ITextureSetGetter>();
        var hadSkinTextureLink = false;
        foreach (var curAddon in addonsToCheck)
        {
            if (curAddon.SkinTexture is null)
            {
                continue;
            }

            var maleSet = curAddon.SkinTexture.Male;
            var femaleSet = curAddon.SkinTexture.Female;
            if ((maleSet is not null && !maleSet.FormKey.IsNull) || (femaleSet is not null && !femaleSet.FormKey.IsNull))
            {
                hadSkinTextureLink = true;
            }

            foreach (var link in new[] { maleSet, femaleSet })
            {
                if (link is null || link.FormKey.IsNull || !index.TryResolveTextureSet(link.FormKey, out var textureSet))
                {
                    continue;
                }

                textureSets.Add(textureSet);
            }
        }

        if (textureSets.Count == 0)
        {
            if (hadSkinTextureLink)
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
