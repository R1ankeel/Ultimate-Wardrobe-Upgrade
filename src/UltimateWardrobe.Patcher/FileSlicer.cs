using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UltimateWardrobe.Core.Abstractions;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Patcher;

/// <summary>
/// Sprint 5.2 - the file-slicing outcome for one <see cref="FileSlicer.Slice"/> run:
/// the game-relative paths actually written (distinct, ordinal - the whole-export dedup set),
/// total copied bytes, skipped mappings, and every non-fatal warning (missing files, skipped
/// mappings). The Sprint 5.3 orchestrator merges these into <see cref="PatchReport"/>.
/// </summary>
public sealed class SliceResult
{
    /// <summary>The game-relative output paths copied, ordered ordinally, each exactly once.</summary>
    public required IReadOnlyList<string> CopiedFiles { get; init; }

    public required long CopiedBytes { get; init; }

    /// <summary>Mapped mappings whose primary mesh could not be located (their files were not copied).</summary>
    public required int SkippedMappings { get; init; }

    public required IReadOnlyList<PatchWarning> Warnings { get; init; }
}

/// <summary>
/// Sprint 5.2 (plan section 4.5, amendment #8) - slices exactly the files a mapping needs from the
/// <see cref="DonorLibrary"/> and copies them under the caller's mod folder, preserving the
/// game-relative path. Per mapping: the effective donor mesh (from the same amendment #8 decision
/// as the plugin writer) + its <c>_1st</c>/<c>_1stperson</c> alternates, the piece's provided
/// textures (folder-mirror fallback when empty), matching BodySlide xml/osp files, physics files
/// (the attached physics patch's <c>SKSE/Plugins/**</c> content, else the donor's matching
/// detection), and the body-then-physics patch overlays (colliding paths, body/skse content and
/// mirrored meshes - last wins). The whole export is de-duplicated per output-relative path and
/// copied once. A missing primary mesh skips the mapping with a warning; other missing files warn
/// and are skipped; cancellation is honored between mappings and between copies. A physical copy
/// failure (e.g. a locked destination) surfaces as a typed <see cref="PatchException"/>.
/// </summary>
public sealed class FileSlicer
{
    private const string MeshesStem = "meshes/";
    private const string TexturesStem = "textures/";
    private const string BodySlideRoot = "CalienteTools/BodySlide";
    private const string SksPluginsRoot = "SKSE/Plugins";

    private readonly ILogger<FileSlicer> _logger;
    private readonly PluginBuilder _meshResolver;

    public FileSlicer(ILogger<FileSlicer>? logger = null)
    {
        _logger = logger ?? NullLogger<FileSlicer>.Instance;
        _meshResolver = new PluginBuilder();
    }

    public SliceResult Slice(
        IReadOnlyList<PieceMapping> mappings,
        DonorLibrary library,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        if (mappings is null) throw new ArgumentNullException(nameof(mappings));
        if (library is null) throw new ArgumentNullException(nameof(library));
        if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("OutputDir must not be empty.", nameof(outputDir));

        _logger.LogInformation(
            "File slicing started for {MappingCount} mappings into '{OutputDir}'",
            mappings.Count,
            outputDir);

        var state = new SliceState();
        var locators = new Dictionary<Guid, DonorFileLocator>(library.Assets.Count);

        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mapping.Status != MappingStatus.Mapped)
            {
                _logger.LogDebug("Mapping {UniqueKey} is not mapped and produces no files; skipped", mapping.UniqueKey);
                continue;
            }

            SliceOne(mapping, library, locators, state, cancellationToken);
        }

        var copiedFiles = new List<string>(state.Export.Count);
        long copiedBytes = 0;
        foreach (var (outputPath, source) in state.Export)
        {
            cancellationToken.ThrowIfCancellationRequested();
            copiedBytes += CopyFile(outputPath, source, outputDir);
            copiedFiles.Add(outputPath);
        }

        _logger.LogInformation(
            "File slicing finished: {Copied} files, {Bytes} bytes, {Skipped} skipped mappings, {Warnings} warnings",
            copiedFiles.Count,
            copiedBytes,
            state.SkippedMappings,
            state.Warnings.Count);

        return new SliceResult
        {
            CopiedFiles = copiedFiles,
            CopiedBytes = copiedBytes,
            SkippedMappings = state.SkippedMappings,
            Warnings = state.Warnings,
        };
    }

    private void SliceOne(
        PieceMapping mapping,
        DonorLibrary library,
        IDictionary<Guid, DonorFileLocator> locators,
        SliceState state,
        CancellationToken cancellationToken)
    {
        var effective = _meshResolver.ResolveEffectiveMesh(mapping, library);

        if (library.Assets.FirstOrDefault(a => a.ImportId == effective.DonorAssetId) is not { } donor)
        {
            SkipMapping(state, mapping, $"the mapped donor asset '{effective.DonorAssetId}' is not in the project library.");
            return;
        }

        if (library.Assets.FirstOrDefault(a => a.ImportId == effective.MeshProviderAssetId) is not { } provider)
        {
            SkipMapping(state, mapping, $"the mesh-provider asset '{effective.MeshProviderAssetId}' is not in the project library.");
            return;
        }

        var donorLocator = LocatorFor(donor, locators);
        var providerLocator = LocatorFor(provider, locators);

        // 1. The effective mesh (amendment #8: the provider asset may be a body/physics patch).
        if (providerLocator.TryLocate(effective.MeshPath) is not string primary)
        {
            SkipMapping(
                state,
                mapping,
                $"the donor mesh '{effective.MeshPath}' was not found in asset '{effective.MeshProviderAssetId}'.");
            return;
        }

        Add(state, effective.MeshPath, effective.MeshProviderAssetId, primary);

        // 2. _1st/_1stperson alternates of the same piece stem in the same meshes folder.
        var donorMeshDir = Path.GetDirectoryName(effective.MeshPath)?.Replace('\\', '/') ?? string.Empty;
        var donorToken = MatchStem(Path.GetFileNameWithoutExtension(effective.MeshPath));
        var firstPersonAlternates = FirstPersonAlternates(donor, donorMeshDir, donorToken, effective.MeshPath);
        foreach (var alternate in firstPersonAlternates)
        {
            AddLocate(state, mapping, alternate, donor.ImportId, donorLocator);
        }

        // 3. Textures: the provided-set piece's paths, else the folder-mirror fallback.
        foreach (var texture in TexturesFor(donor, mapping.DonorPieceEditorId, effective.MeshPath))
        {
            AddLocate(state, mapping, texture, donor.ImportId, donorLocator);
        }

        // 4. BodySlide: detected files whose name matches the piece stem or the target set token.
        foreach (var detected in donor.DetectedBodySlideFiles)
        {
            if (MatchesPiece(detected, donorToken, mapping.TargetArmorSetId))
            {
                AddLocate(state, mapping, PatchPathRules.ToGameRelative(detected), donor.ImportId, donorLocator);
            }
        }

        // 5. Physics: the attached physics patch's SKSE/Plugins content, else the donor's matching
        //    detected physics files (a physics patch replaces the donor's own physics layer).
        var physicsPatch = mapping.PhysicsPatchAssetId is { } physicsId
            ? library.Assets.FirstOrDefault(a => a.ImportId == physicsId)
            : null;
        if (physicsPatch is not null)
        {
            var patchLocator = LocatorFor(physicsPatch, locators);
            foreach (var entry in physicsPatch.FileManifest)
            {
                var relative = PatchPathRules.ToGameRelative(entry.RelativePath);
                if (IsUnder(relative, SksPluginsRoot))
                {
                    AddLocate(state, mapping, relative, physicsPatch.ImportId, patchLocator);
                }
            }
        }
        else
        {
            foreach (var detected in donor.DetectedPhysicsFiles)
            {
                if (MatchesPiece(detected, donorToken, mapping.TargetArmorSetId))
                {
                    AddLocate(state, mapping, PatchPathRules.ToGameRelative(detected), donor.ImportId, donorLocator);
                }
            }
        }

        // 6. Patch overlays, body first then physics (last wins). A patch entry is taken when it
        //    collides with an already-sliced output path, lives under CalienteTools/BodySlide or
        //    SKSE/Plugins, or mirrors the effective mesh or one of its _1st alternates.
        var mirrorPaths = new HashSet<string>(StringComparer.Ordinal) { effective.MeshPath };
        foreach (var alternate in firstPersonAlternates)
        {
            mirrorPaths.Add(alternate);
        }

        AddPatchOverlay(state, mapping, library, locators, mapping.BodyConversionPatchAssetId, mirrorPaths, cancellationToken);
        AddPatchOverlay(state, mapping, library, locators, mapping.PhysicsPatchAssetId, mirrorPaths, cancellationToken);
    }

    private void AddPatchOverlay(
        SliceState state,
        PieceMapping mapping,
        DonorLibrary library,
        IDictionary<Guid, DonorFileLocator> locators,
        Guid? patchAssetId,
        ISet<string> mirrorPaths,
        CancellationToken cancellationToken)
    {
        if (patchAssetId is null)
        {
            return;
        }

        var patch = library.Assets.FirstOrDefault(a => a.ImportId == patchAssetId.Value);
        if (patch is null)
        {
            // Consistent with the amendment #8 mesh decision (a missing patch asset is ignored).
            return;
        }

        var locator = LocatorFor(patch, locators);
        foreach (var entry in patch.FileManifest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = PatchPathRules.ToGameRelative(entry.RelativePath);
            var belongs = state.Export.ContainsKey(relative)
                || IsUnder(relative, BodySlideRoot)
                || IsUnder(relative, SksPluginsRoot)
                || mirrorPaths.Contains(relative);
            if (!belongs)
            {
                continue;
            }

            AddLocate(state, mapping, relative, patch.ImportId, locator);
        }
    }

    private static void AddLocate(
        SliceState state,
        PieceMapping mapping,
        string outputPath,
        Guid assetId,
        DonorFileLocator locator)
    {
        if (locator.TryLocate(outputPath) is not string physical)
        {
            state.Warnings.Add(new PatchWarning(
                $"Mapping {mapping.UniqueKey}: file '{outputPath}' was not found in donor asset '{assetId}' and was not copied.",
                mapping.TargetPieceEditorId));
            return;
        }

        Add(state, outputPath, assetId, physical);
    }

    private static void Add(SliceState state, string outputPath, Guid assetId, string physicalPath)
    {
        state.Export[outputPath] = new SliceSource(assetId, physicalPath);
    }

    private static void SkipMapping(SliceState state, PieceMapping mapping, string reason)
    {
        state.SkippedMappings++;
        state.Warnings.Add(new PatchWarning(
            $"Mapping {mapping.UniqueKey} was skipped by the file slicer: {reason}",
            mapping.TargetPieceEditorId));
    }

    private static long CopyFile(string outputPath, SliceSource source, string outputDir)
    {
        var destination = Path.GetFullPath(Path.Combine(outputDir, outputPath.Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source.PhysicalPath, destination, overwrite: true);
            return new FileInfo(source.PhysicalPath).Length;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PatchException($"Could not copy '{outputPath}' into the export folder: {ex.Message}", ex);
        }
    }

    private static DonorFileLocator LocatorFor(
        DonorAsset asset,
        IDictionary<Guid, DonorFileLocator> locators)
    {
        if (!locators.TryGetValue(asset.ImportId, out var locator))
        {
            locator = new DonorFileLocator(asset.ExtractedPath);
            locators.Add(asset.ImportId, locator);
        }

        return locator;
    }

    // -- per-mapping selection rules -------------------------------------------------------------

    private static IReadOnlyList<string> TexturesFor(DonorAsset donor, string donorPieceEditorId, string meshPath)
    {
        var provided = ProvidedPieceTextures(donor, donorPieceEditorId);
        return provided.Count > 0 ? provided : MirrorFolderTextures(donor, meshPath);
    }

    /// <summary>Provided-set textures for the mapped donor piece (game-relative, ordinal).</summary>
    private static IReadOnlyList<string> ProvidedPieceTextures(DonorAsset donor, string donorPieceEditorId)
    {
        foreach (var providedSet in donor.ProvidedSets)
        {
            foreach (var variant in providedSet.Variants)
            {
                foreach (var piece in variant.Pieces)
                {
                    if (string.Equals(piece.EditorId, donorPieceEditorId, StringComparison.Ordinal))
                    {
                        return piece.TexturePaths
                            .OrderBy(t => t, StringComparer.Ordinal)
                            .ToList();
                    }
                }
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Fallback when the provided set yields no textures: every <c>textures/**/*.dds</c> manifest
    /// entry whose folder mirrors the donor mesh folder (the path tail below the <c>meshes/</c> stem).
    /// </summary>
    private static IReadOnlyList<string> MirrorFolderTextures(DonorAsset donor, string meshPath)
    {
        var meshDir = Path.GetDirectoryName(meshPath)?.Replace('\\', '/') ?? string.Empty;
        if (!meshDir.StartsWith(MeshesStem, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var mirrorDir = TexturesStem + meshDir[MeshesStem.Length..];
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in donor.FileManifest)
        {
            var relative = PatchPathRules.ToGameRelative(entry.RelativePath);
            if (!relative.StartsWith(TexturesStem, StringComparison.OrdinalIgnoreCase)
                || !relative.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryDir = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
            if (PatchPathRules.EqualsNormalized(entryDir, mirrorDir))
            {
                result.Add(relative);
            }
        }

        return result.ToList();
    }

    /// <summary>
    /// <c>_1st</c>/<c>_1stperson</c> alternates of the same piece stem in the same meshes folder,
    /// straight from the donor manifest (ordinal, game-relative). The primary mesh itself is never
    /// duplicated.
    /// </summary>
    private static IReadOnlyList<string> FirstPersonAlternates(
        DonorAsset donor,
        string donorMeshDir,
        string donorToken,
        string effectiveMeshPath)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in donor.FileManifest)
        {
            var relative = PatchPathRules.ToGameRelative(entry.RelativePath);
            if (PatchPathRules.EqualsNormalized(relative, effectiveMeshPath))
            {
                continue;
            }

            var entryDir = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
            if (!PatchPathRules.EqualsNormalized(entryDir, donorMeshDir))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(relative);
            if (!stem.Contains("_1st", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(MatchStem(stem), donorToken, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(relative);
        }

        return result.ToList();
    }

    /// <summary>
    /// Strips trailing <c>_1stperson</c>/<c>_1st_person</c>/<c>_1st</c>/<c>_0</c>/<c>_1</c> weight
    /// and first-person markers so <c>cuirass</c>, <c>cuirass_1</c>, <c>cuirass_1st</c> and
    /// <c>cuirass_1stperson</c> collapse to one piece token (mirrors the classifier's base-stem
    /// rule, implemented locally so the Patcher stays coupled to Core+Scanner only per
    /// amendment #4).
    /// </summary>
    private static string MatchStem(string stem)
    {
        var result = stem;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var marker in new[] { "_1stperson", "_1st_person", "_1st", "_0", "_1" })
            {
                if (result.EndsWith(marker, StringComparison.OrdinalIgnoreCase) && result.Length > marker.Length)
                {
                    result = result[..^marker.Length];
                    changed = true;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>BodySlide/physics file matches when its name contains the piece token or the target set token.</summary>
    private static bool MatchesPiece(string detectedPath, string donorToken, string targetSetId)
    {
        var fileName = Path.GetFileNameWithoutExtension(detectedPath);
        return fileName.Contains(donorToken, StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(SetToken(targetSetId), StringComparison.OrdinalIgnoreCase);
    }

    private static string SetToken(string setId)
    {
        return new string(setId.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static bool IsUnder(string gameRelativePath, string root)
    {
        return gameRelativePath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SliceState
    {
        public SortedDictionary<string, SliceSource> Export { get; } = new(StringComparer.Ordinal);

        public List<PatchWarning> Warnings { get; } = new();

        public int SkippedMappings { get; set; }
    }

    private sealed record SliceSource(Guid AssetId, string PhysicalPath);
}