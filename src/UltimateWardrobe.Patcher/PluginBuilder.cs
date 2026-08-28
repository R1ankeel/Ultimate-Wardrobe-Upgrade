using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.Patcher;

/// <summary>
/// The amendment #6 path comparison and game-relative conversion rules shared by the plugin
/// writer and (from Sprint 5.2) the file slicer. Equality is NORMALIZED on both sides exactly as
/// the amendment requires: backslash -> forward slash, ordinal case-insensitive, trimmed. A game
/// relative path strips a leading <c>Data/</c> segment ("root-or-Data layout", like the Phase 1
/// <see cref="KeyNormalizer"/> consumption), so a manifest entry <c>Data/meshes/x.nif</c> equals a
/// donor path <c>meshes/x.nif</c>.
/// </summary>
public static class PatchPathRules
{
    /// <summary>
    /// Slash/trim normalization for WRITTEN paths (backslash -> forward slash, trimmed, original
    /// case preserved). The value stored on the esp record.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Replace('\\', '/');
    }

    /// <summary>
    /// Amendment #6 equality: <c>\</c> -> <c>/</c>, ordinal case-insensitive (lowercased), trimmed,
    /// on BOTH sides. A null current <c>Model.File</c> never equals any path - callers handle that
    /// before comparing.
    /// </summary>
    public static bool EqualsNormalized(string a, string b)
    {
        return string.Equals(
            Normalize(a).ToLowerInvariant(),
            Normalize(b).ToLowerInvariant(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Maps a manifest relative path to its game-relative form: slash/trim normalized and a leading
    /// <c>Data/</c> segment stripped (ordinal case-insensitive).
    /// </summary>
    public static string ToGameRelative(string relativePath)
    {
        var normalized = Normalize(relativePath);
        const string dataPrefix = "data/";
        if (normalized.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[dataPrefix.Length..];
        }

        return normalized;
    }
}

/// <summary>
/// The amendment #8 effective-mesh decision for one mapping: the game-relative path the generated
/// esp points at, and WHICH donor asset physically supplies the file. When a body/physics patch
/// asset's manifest shadows the donor mesh at the same game-relative path the patch file wins (last
/// wins, physics evaluated after body); otherwise the donor mesh stays. The build (Sprint 5.1.1)
/// writes <see cref="MeshPath"/> into the gender-specific WorldModel slots; the file slicer
/// (Sprint 5.2) resolves <see cref="MeshProviderAssetId"/> to the physical file.
/// </summary>
public sealed class EffectiveMeshResult
{
    /// <summary>The normalized game-relative mesh path (the final esp <c>WorldModel</c> path).</summary>
    public required string MeshPath { get; init; }

    /// <summary>The donor or patch asset that owns the physical file (last-wins over the donor).</summary>
    public required Guid MeshProviderAssetId { get; init; }

    /// <summary>The mapping's own donor asset.</summary>
    public required Guid DonorAssetId { get; init; }

    public bool ShadowedByBodyPatch { get; init; }

    public bool ShadowedByPhysicsPatch { get; init; }
}

/// <summary>
/// Sprint 5.1 - the Mutagen output esp writer (plan section 4.4). Per resolved mapping it
/// <c>GetOrAddAsOverride</c>s the target ARMA, writes the effective donor mesh path into the
/// gender-specific <c>WorldModel</c> slot (creating null slots, never dereferencing), prefixes the
/// <c>EditorID</c> with <c>UW_</c>, applies the source-kind-driven ESL gate (amendment #7, resolved
/// to Mutagen's <see cref="Plugins.Records.IMod.IsSmallMaster"/> member) and the amendment #6
/// loose-path skip, and writes the esp with Mutagen's auto-collected masters. The source ARMA
/// getters are re-loaded over the Phase 1 pipeline (Scanner reuse, amendment #4) and owned for the
/// duration of the build, so nothing outlives the call.
/// </summary>
public sealed class PluginBuilder
{
    private readonly ILogger<PluginBuilder> _logger;

    public PluginBuilder(ILogger<PluginBuilder>? logger = null)
    {
        _logger = logger ?? NullLogger<PluginBuilder>.Instance;
    }

    /// <summary>
    /// Resolves the amendment #8 effective mesh path and its provider asset for one mapping, over
    /// the mapped donor plus the attached body/physics patch manifests.
    /// </summary>
    public EffectiveMeshResult ResolveEffectiveMesh(PieceMapping mapping, DonorLibrary library)
    {
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        if (library is null) throw new ArgumentNullException(nameof(library));

        var donorPath = PatchPathRules.Normalize(mapping.DonorMeshPath);
        var provider = mapping.DonorAssetId;
        var shadowedByBody = false;
        var shadowedByPhysics = false;

        if (mapping.BodyConversionPatchAssetId is { } bodyId
            && TryFindAsset(library, bodyId) is { } body
            && ManifestShadows(body, donorPath))
        {
            provider = bodyId;
            shadowedByBody = true;
        }

        if (mapping.PhysicsPatchAssetId is { } physicsId
            && TryFindAsset(library, physicsId) is { } physics
            && ManifestShadows(physics, donorPath))
        {
            provider = physicsId;
            shadowedByPhysics = true;
        }

        return new EffectiveMeshResult
        {
            MeshPath = donorPath,
            MeshProviderAssetId = provider,
            DonorAssetId = mapping.DonorAssetId,
            ShadowedByBodyPatch = shadowedByBody,
            ShadowedByPhysicsPatch = shadowedByPhysics,
        };
    }

    /// <summary>
    /// Builds the output esp from the resolved targets and writes it under <paramref name="outputDir"/>.
    /// Masters come from Mutagen's automatic collection (the esp re-opens listing the source key);
    /// the ESL flag is set for a Vanilla+DLC source and never for a StoryMod source. The returned
    /// <see cref="PatchResult.PluginPath"/> is the written esp; <see cref="PatchReport.CopiedFiles"/>
    /// stays empty until the Sprint 5.2 slicer fills it.
    /// </summary>
    public PatchResult Build(
        Overhaul overhaul,
        IReadOnlyList<ResolvedTarget> targets,
        DonorLibrary library,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        if (overhaul is null) throw new ArgumentNullException(nameof(overhaul));
        if (targets is null) throw new ArgumentNullException(nameof(targets));
        if (library is null) throw new ArgumentNullException(nameof(library));
        if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("OutputDir must not be empty.", nameof(outputDir));

        cancellationToken.ThrowIfCancellationRequested();

        var modKey = ModKey.FromFileName($"UltimateWardrobe - {overhaul.Name}.esp");
        var outputMod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        var isEsl = overhaul.Catalog?.Source.Kind == CatalogSourceKind.VanillaPlusDlc;
        if (isEsl)
        {
            ((IMod)outputMod).IsSmallMaster = true;
        }

        _logger.LogInformation(
            "Plugin '{PluginFileName}' build started for Overhaul '{OverhaulName}' over {TargetCount} resolved targets (ESL gate {IsEsl})",
            modKey.FileName,
            overhaul.Name,
            targets.Count,
            isEsl);

        var warnings = new List<ScanWarning>();
        var source = LoadSource(overhaul, warnings, cancellationToken);
        var overriddenKeys = new HashSet<FormKey>();
        var warningsOut = new List<PatchWarning>();

        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var mapping = target.Mapping;
                if (mapping.Status != MappingStatus.Mapped)
                {
                    continue;
                }

                var effective = ResolveEffectiveMesh(mapping, library);
                var (maleNeedsWrite, femaleNeedsWrite) = GenderedSlotsNeedingWrite(target, effective.MeshPath);

                if (!maleNeedsWrite && !femaleNeedsWrite)
                {
                    var message =
                        $"Mapping {mapping.UniqueKey} wrote no ARMA override: the effective donor mesh '{effective.MeshPath}' " +
                        "equals the target ARMA's current model path, so the loose file already wins (amendment #6).";
                    _logger.LogInformation(message);
                    warningsOut.Add(new PatchWarning(message, mapping.TargetPieceEditorId));
                    continue;
                }

                if (!source.Index.TryResolveArmorAddon(target.ArmorAddonKey, out var armaGetter))
                {
                    var message =
                        $"Mapping {mapping.UniqueKey} wrote no ARMA override: target ARMA '{target.ArmorAddonKey}' " +
                        "could not be loaded from the source plugins.";
                    _logger.LogWarning(message);
                    warningsOut.Add(new PatchWarning(message, mapping.TargetPieceEditorId));
                    continue;
                }

                var isNewOverride = overriddenKeys.Add(target.ArmorAddonKey);
                var overrideArma = outputMod.ArmorAddons.GetOrAddAsOverride(armaGetter);

                if (overrideArma.EditorID is { } editorId
                    && !editorId.StartsWith("UW_", StringComparison.Ordinal))
                {
                    overrideArma.EditorID = "UW_" + editorId;
                }

                if (maleNeedsWrite)
                {
                    SetWorldModelSide(overrideArma, male: true, effective.MeshPath);
                }

                if (femaleNeedsWrite)
                {
                    SetWorldModelSide(overrideArma, male: false, effective.MeshPath);
                }

                if (isNewOverride)
                {
                    _logger.LogDebug(
                        "Mapping {UniqueKey}: ARMA {ArmorAddonKey} overridden with mesh '{MeshPath}' (male {Male}, female {Female})",
                        mapping.UniqueKey,
                        target.ArmorAddonKey,
                        effective.MeshPath,
                        maleNeedsWrite,
                        femaleNeedsWrite);
                }
            }

            Directory.CreateDirectory(outputDir);
            var pluginPath = Path.Combine(outputDir, modKey.FileName);
            outputMod.WriteToBinary(pluginPath);

            _logger.LogInformation(
                "Plugin '{PluginFileName}' written with {OverriddenCount} overridden ARMA record(s)",
                modKey.FileName,
                overriddenKeys.Count);

            return new PatchResult(pluginPath, Array.Empty<string>())
            {
                Report = new PatchReport
                {
                    TotalMappings = targets.Count,
                    ResolvedMappings = targets.Count,
                    OverriddenRecords = overriddenKeys.Count,
                    Warnings = warningsOut
                        .Concat(warnings.Select(w => new PatchWarning(w.Message, w.EditorId)))
                        .ToList(),
                },
            };
        }
        catch (Exception ex) when (ex is not PatchException and not OperationCanceledException)
        {
            throw new PatchException($"Could not write the output plugin '{modKey.FileName}': {ex.Message}", ex);
        }
        finally
        {
            foreach (var mod in source.Loaded)
            {
                mod.Dispose();
            }

            _logger.LogInformation(
                "Plugin build finished for Overhaul '{OverhaulName}': {Overridden} overrides, {WarningCount} warnings, {SourceWarnings} source-load warnings",
                overhaul.Name,
                overriddenKeys.Count,
                warningsOut.Count,
                warnings.Count);
        }
    }

    private static (bool MaleNeedsWrite, bool FemaleNeedsWrite) GenderedSlotsNeedingWrite(
        ResolvedTarget target,
        string effectiveMesh)
    {
        return target.Gender switch
        {
            Gender.Male => (SlotNeedsWrite(effectiveMesh, target.CurrentModelMalePath), false),
            Gender.Female => (false, SlotNeedsWrite(effectiveMesh, target.CurrentModelFemalePath)),
            Gender.Unisex => (
                SlotNeedsWrite(effectiveMesh, target.CurrentModelMalePath),
                SlotNeedsWrite(effectiveMesh, target.CurrentModelFemalePath)),
            _ => (false, false),
        };
    }

    private static bool SlotNeedsWrite(string effectiveMesh, string? currentPath)
    {
        // A null current Model.File never equals any path - the override is written (amendment #6).
        if (currentPath is null)
        {
            return true;
        }

        return !PatchPathRules.EqualsNormalized(effectiveMesh, currentPath);
    }

    private static void SetWorldModelSide(ArmorAddon addon, bool male, string path)
    {
        // Create the GenderedItem and/or the slot Model rather than dereferencing a null side.
        var worldModel = addon.WorldModel;
        if (worldModel is null)
        {
            worldModel = new GenderedItem<Model?>(null, null);
            addon.WorldModel = worldModel;
        }

        var model = male ? worldModel.Male : worldModel.Female;
        if (model is null)
        {
            model = new Model();
            if (male)
            {
                worldModel.Male = model;
            }
            else
            {
                worldModel.Female = model;
            }
        }

        var link = new AssetLink<SkyrimModelAssetType>();
        if (!link.TrySetPath(path))
        {
            throw new PatchException($"Could not set the WorldModel path '{path}' on ARMA '{addon.EditorID}' ({addon.FormKey}).");
        }

        model.File = link;
    }

    private sealed class LoadedSource
    {
        public required RecordIndex Index { get; init; }

        public required IReadOnlyList<LoadedMod> Loaded { get; init; }
    }

    private static LoadedSource LoadSource(
        Overhaul overhaul,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken)
    {
        var discovery = Discover(overhaul.Catalog?.Source ?? overhaul.Source, warnings);
        var loader = new ModLoader();
        var loadOrder = new LoadOrderBuilder(loader).Build(discovery, warnings, cancellationToken);

        var loaded = new List<LoadedMod>();
        foreach (var plugin in loadOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mod = loader.TryLoad(plugin.AbsolutePath, warnings);
            if (mod is null)
            {
                continue;
            }

            loaded.Add(mod);
        }

        return new LoadedSource
        {
            Index = RecordIndex.Build(loaded, warnings, cancellationToken),
            Loaded = loaded,
        };
    }

    private static DiscoveryResult Discover(CatalogSource source, List<ScanWarning> warnings)
    {
        try
        {
            return new PluginDiscovery().Discover(source, warnings);
        }
        catch (Exception ex)
        {
            throw new PatchException($"Could not load the source for building the plugin: {ex.Message}", ex);
        }
    }

    private static DonorAsset? TryFindAsset(DonorLibrary library, Guid id)
    {
        return library.Assets.FirstOrDefault(a => a.ImportId == id);
    }

    private static bool ManifestShadows(DonorAsset patch, string normalizedDonorPath)
    {
        foreach (var entry in patch.FileManifest)
        {
            if (PatchPathRules.EqualsNormalized(PatchPathRules.ToGameRelative(entry.RelativePath), normalizedDonorPath))
            {
                return true;
            }
        }

        return false;
    }
}