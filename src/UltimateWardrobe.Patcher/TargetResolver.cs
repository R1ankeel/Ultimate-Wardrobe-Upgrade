using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Patcher;

/// <summary>
/// One catalog target resolved to live source records (Sprint 5.0.3, plan 4.3). Carries the
/// <see cref="PieceMapping"/> plus the resolved ARMO/ARMA <see cref="FormKey"/>s and the target
/// ARMA's current <see cref="Model"/> file paths (normalized to forward slashes, matching the
/// Phase 1 <c>GivenPath</c> normalization) so the Phase 5 plugin writer can apply the override
/// and the amendment #6 loose-path skip. Data is copied out before the scanner overlays are
/// disposed, so no getter escapes the resolver.
/// </summary>
public sealed class ResolvedTarget
{
    public required PieceMapping Mapping { get; init; }
    public required FormKey ArmorKey { get; init; }
    public required FormKey ArmorAddonKey { get; init; }
    public string? CurrentModelMalePath { get; init; }
    public string? CurrentModelFemalePath { get; init; }
    public Gender Gender { get; init; }
}

/// <summary>
/// The outcome of a <see cref="TargetResolver.Resolve"/> run: resolved targets in mapping order
/// plus every warning the source load and the per-mapping lookups produced (converted from the
/// Phase 1 <see cref="ScanWarning"/> stream).
/// </summary>
public sealed class TargetResolutionResult
{
    public required IReadOnlyList<ResolvedTarget> Targets { get; init; }
    public required IReadOnlyList<PatchWarning> Warnings { get; init; }
}

/// <summary>
/// Resolves an <see cref="Overhaul"/>'s <see cref="PieceMapping"/>s to live (ARMO, ARMA) records
/// over the Phase 1 loading pipeline (Sprint 5.0.3, roadmap 7.5 task 5.2). Reuses
/// <see cref="PluginDiscovery"/> + <see cref="LoadOrderBuilder"/> + <see cref="ModLoader"/> +
/// <see cref="RecordIndex"/>; the catalog piece is located by (set, gender, EditorId), the ARMO by
/// EditorId primary with a FormId fallback, and the ARMA by <see cref="Piece.ArmaEditorId"/> with a
/// first-armature-addon fallback. Build-blocking: missing <see cref="Overhaul.Catalog"/> or an
/// unreadable source root throw a typed <see cref="PatchException"/>. Per-mapping: unresolved
/// targets are skipped with a <see cref="PatchWarning"/> and the build continues.
/// </summary>
public sealed class TargetResolver
{
    private readonly ILogger<TargetResolver> _logger;

    public TargetResolver(ILogger<TargetResolver>? logger = null)
    {
        _logger = logger ?? NullLogger<TargetResolver>.Instance;
    }

    public TargetResolutionResult Resolve(Overhaul overhaul, CancellationToken cancellationToken = default)
    {
        if (overhaul is null) throw new ArgumentNullException(nameof(overhaul));
        if (overhaul.Catalog is null)
        {
            throw new PatchException(
                $"Overhaul '{overhaul.Name}' has no scanned catalog attached. " +
                "Run a scan of its source and save the project before patching (Phase 5, amendment #5).");
        }

        _logger.LogInformation(
            "Target resolution started for Overhaul '{OverhaulName}' over {MappingCount} mappings",
            overhaul.Name,
            overhaul.Mappings.Count);

        var warnings = new List<ScanWarning>();
        var discovery = Discover(overhaul.Catalog.Source, warnings);
        cancellationToken.ThrowIfCancellationRequested();

        var loader = new ModLoader();
        var loadOrder = new LoadOrderBuilder(loader).Build(discovery, warnings, cancellationToken);

        var armorByEditorId = new Dictionary<string, IArmorGetter>(StringComparer.Ordinal);
        var armaByEditorId = new Dictionary<string, IArmorAddonGetter>(StringComparer.Ordinal);
        var loaded = new List<LoadedMod>();

        try
        {
            foreach (var plugin in loadOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mod = loader.TryLoad(plugin.AbsolutePath, warnings);
                if (mod is null)
                {
                    _logger.LogWarning(
                        "Target resolution: plugin '{PluginPath}' failed to load and was skipped",
                        plugin.AbsolutePath);
                    continue;
                }

                loaded.Add(mod);
                IndexEditorIds(mod, armorByEditorId, armaByEditorId, warnings);
            }

            var index = RecordIndex.Build(loaded, warnings, cancellationToken);
            var targets = ResolveTargets(
                overhaul.Catalog,
                overhaul.Mappings,
                index,
                loaded.Select(m => m.ModKey).ToList(),
                armorByEditorId,
                armaByEditorId,
                warnings,
                cancellationToken);

            _logger.LogInformation(
                "Target resolution finished for Overhaul '{OverhaulName}': {Resolved}/{Total} mappings resolved, {WarningCount} warnings",
                overhaul.Name,
                targets.Count,
                overhaul.Mappings.Count,
                warnings.Count);

            return new TargetResolutionResult
            {
                Targets = targets,
                Warnings = warnings.Select(w => new PatchWarning(w.Message, w.EditorId)).ToList(),
            };
        }
        finally
        {
            foreach (var mod in loaded)
            {
                mod.Dispose();
            }
        }
    }

    private static DiscoveryResult Discover(CatalogSource source, List<ScanWarning> warnings)
    {
        try
        {
            return new PluginDiscovery().Discover(source, warnings);
        }
        catch (Exception ex)
        {
            throw new PatchException($"Could not load the source for patching: {ex.Message}", ex);
        }
    }

    private static void IndexEditorIds(
        LoadedMod mod,
        IDictionary<string, IArmorGetter> armorByEditorId,
        IDictionary<string, IArmorAddonGetter> armaByEditorId,
        List<ScanWarning> warnings)
    {
        try
        {
            foreach (var entry in mod.Overlay.Armors.RecordCache)
            {
                if (entry.Value.EditorID is not null)
                {
                    armorByEditorId[entry.Value.EditorID] = entry.Value;
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(EditorIdIndexWarning(mod, ex));
        }

        try
        {
            foreach (var entry in mod.Overlay.ArmorAddons.RecordCache)
            {
                if (entry.Value.EditorID is not null)
                {
                    armaByEditorId[entry.Value.EditorID] = entry.Value;
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(EditorIdIndexWarning(mod, ex));
        }
    }

    private static ScanWarning EditorIdIndexWarning(LoadedMod mod, Exception ex)
    {
        return new ScanWarning(
            $"EditorID index of plugin '{mod.AbsolutePath}' could not be built and was skipped: {ex.Message}");
    }

    private static IReadOnlyList<ResolvedTarget> ResolveTargets(
        Catalog catalog,
        IReadOnlyList<PieceMapping> mappings,
        RecordIndex index,
        IReadOnlyList<ModKey> loadedModKeys,
        IReadOnlyDictionary<string, IArmorGetter> armorByEditorId,
        IReadOnlyDictionary<string, IArmorAddonGetter> armaByEditorId,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken)
    {
        var targets = new List<ResolvedTarget>();

        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveOne(
                mapping,
                catalog,
                index,
                loadedModKeys,
                armorByEditorId,
                armaByEditorId,
                targets,
                warnings);
        }

        return targets;
    }

    private static void ResolveOne(
        PieceMapping mapping,
        Catalog catalog,
        RecordIndex index,
        IReadOnlyList<ModKey> loadedModKeys,
        IReadOnlyDictionary<string, IArmorGetter> armorByEditorId,
        IReadOnlyDictionary<string, IArmorAddonGetter> armaByEditorId,
        ICollection<ResolvedTarget> targets,
        List<ScanWarning> warnings)
    {
        var piece = FindCatalogPiece(catalog, mapping);
        if (piece is null)
        {
            warnings.Add(Unresolved(mapping, "the target piece is not present in the overlay's catalog."));
            return;
        }

        var armor = FindArmor(piece, mapping, index, loadedModKeys, armorByEditorId);
        if (armor is null)
        {
            warnings.Add(Unresolved(mapping, $"ARMO '{piece.EditorId}' (or FormId {piece.FormId}) was not found in the source files."));
            return;
        }

        var addon = FindArmorAddon(piece, armor, index, armaByEditorId);
        if (addon is null)
        {
            warnings.Add(Unresolved(mapping, $"ARMA for '{piece.EditorId}' was not found: no '{piece.ArmaEditorId}' match and no armature addon resolved."));
            return;
        }

        targets.Add(new ResolvedTarget
        {
            Mapping = mapping,
            ArmorKey = armor.FormKey,
            ArmorAddonKey = addon.FormKey,
            CurrentModelMalePath = NormalizeModelPath(addon.WorldModel?.Male?.File),
            CurrentModelFemalePath = NormalizeModelPath(addon.WorldModel?.Female?.File),
            Gender = mapping.TargetGender,
        });
    }

    private static Piece? FindCatalogPiece(Catalog catalog, PieceMapping mapping)
    {
        foreach (var set in catalog.Sets)
        {
            if (set.Id != mapping.TargetArmorSetId)
            {
                continue;
            }

            foreach (var variant in set.Variants)
            {
                if (variant.Gender != mapping.TargetGender)
                {
                    continue;
                }

                foreach (var piece in variant.Pieces)
                {
                    if (piece.EditorId == mapping.TargetPieceEditorId)
                    {
                        return piece;
                    }
                }
            }
        }

        return null;
    }

    private static IArmorGetter? FindArmor(
        Piece piece,
        PieceMapping mapping,
        RecordIndex index,
        IReadOnlyList<ModKey> loadedModKeys,
        IReadOnlyDictionary<string, IArmorGetter> armorByEditorId)
    {
        if (armorByEditorId.TryGetValue(piece.EditorId, out var byEditorId))
        {
            return byEditorId;
        }

        foreach (var modKey in loadedModKeys)
        {
            var key = new FormKey(modKey, piece.FormId);
            if (index.TryResolveArmor(key, out var byFormId))
            {
                return byFormId;
            }
        }

        return null;
    }

    private static IArmorAddonGetter? FindArmorAddon(
        Piece piece,
        IArmorGetter armor,
        RecordIndex index,
        IReadOnlyDictionary<string, IArmorAddonGetter> armaByEditorId)
    {
        if (!string.IsNullOrWhiteSpace(piece.ArmaEditorId) && armaByEditorId.TryGetValue(piece.ArmaEditorId, out var byEditorId))
        {
            return byEditorId;
        }

        if (armor.Armature is not null)
        {
            foreach (var link in armor.Armature)
            {
                if (link.IsNull || link.FormKey.IsNull)
                {
                    continue;
                }

                if (index.TryResolveArmorAddon(link.FormKey, out var byArmature))
                {
                    return byArmature;
                }
            }
        }

        return null;
    }

    private static string? NormalizeModelPath(AssetLinkGetter<SkyrimModelAssetType>? link)
    {
        if (link is null || link.IsNull || string.IsNullOrWhiteSpace(link.GivenPath))
        {
            return null;
        }

        return link.GivenPath.Replace('\\', '/');
    }

    private static ScanWarning Unresolved(PieceMapping mapping, string reason)
    {
        return new ScanWarning(
            $"Mapping {mapping.UniqueKey} (target piece '{mapping.TargetPieceEditorId}' in set '{mapping.TargetArmorSetId}', gender {mapping.TargetGender}) was skipped: {reason}",
            mapping.TargetPieceEditorId);
    }
}