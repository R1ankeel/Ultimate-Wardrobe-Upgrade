using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;

namespace UltimateWardrobe.DonorLibrary;

/// <summary>
/// Branch-2 ProvidedSets assembly (Sprints 2.2.2-2.2.4): groups game-relative mesh paths by
/// <see cref="KeyNormalizer.NormalizeMeshFolder"/> into set families, builds one <see cref="Piece"/>
/// per (directory, base stem) - <c>_0</c>/<c>_1</c>/<c>_1st</c> alternates collapse into one piece
/// whose <see cref="Piece.MeshPath"/> is the <c>_1</c> preferred file; the alternates stay in the
/// <see cref="DonorAsset.FileManifest"/> and are never separate pieces. Pieces get gender
/// (<see cref="DonorNameHeuristics.GenderFrom"/>, Unisex fallback) and weight
/// (<see cref="DonorNameHeuristics.WeightFromPath"/>), then assemble into one <see cref="Variant"/>
/// per (gender, weight). Textures are linked by folder key + piece word (2.2.3), deduped and
/// ordinal-ordered, mirroring the branch-1 TXST correlation output. Fully deterministic - sets by
/// Id, variants by (gender, weight), pieces by (slot, EditorId, mesh path).
/// </summary>
public static class MeshSetAssembler
{
    public static IReadOnlyList<DonorProvidedSet> Assemble(
        IReadOnlyList<string> meshPaths,
        IReadOnlyList<string> texturePaths,
        List<ScanWarning>? warnings = null)
    {
        if (meshPaths is null || meshPaths.Count == 0)
        {
            return Array.Empty<DonorProvidedSet>();
        }

        var textureMap = IndexTexturesByKey(texturePaths ?? Array.Empty<string>());

        var groups = new SortedDictionary<string, SetBuild>(StringComparer.Ordinal);
        foreach (var mesh in meshPaths)
        {
            var key = KeyNormalizer.NormalizeMeshFolder(mesh);
            if (key is null)
            {
                continue;
            }

            if (!groups.TryGetValue(key.Id, out var group))
            {
                group = new SetBuild(key.Id, key.DisplayName);
                groups.Add(key.Id, group);
            }

            group.Meshes.Add(mesh);
        }

        var result = new List<DonorProvidedSet>(groups.Count);
        foreach (var group in groups.Values)
        {
            var variants = BuildVariants(group, textureMap);
            if (variants.Count == 0)
            {
                continue;
            }

            result.Add(new DonorProvidedSet(group.Id, group.DisplayName, variants));
        }

        return result;
    }

    private static IReadOnlyList<Variant> BuildVariants(SetBuild group, IReadOnlyDictionary<string, TextureGroup> textureMap)
    {
        var byPiece = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var mesh in group.Meshes)
        {
            var stem = Path.GetFileNameWithoutExtension(mesh);
            var directory = Path.GetDirectoryName(mesh)?.Replace('\\', '/') ?? string.Empty;
            var baseStem = DonorNameHeuristics.BaseStem(stem);
            if (baseStem.Length == 0)
            {
                continue;
            }

            var pieceKey = $"{directory}|{baseStem}";
            if (!byPiece.TryGetValue(pieceKey, out var files))
            {
                files = new List<string>();
                byPiece.Add(pieceKey, files);
            }

            files.Add(mesh);
        }

        var pieces = new List<PieceBuild>(byPiece.Count);
        foreach (var entry in byPiece)
        {
            var baseStem = entry.Key[(entry.Key.IndexOf('|') + 1)..];
            var primary = entry.Value
                .OrderBy(f => DonorNameHeuristics.PrimaryRank(Path.GetFileNameWithoutExtension(f), baseStem))
                .ThenBy(f => f, StringComparer.Ordinal)
                .First();
            var primaryStem = Path.GetFileNameWithoutExtension(primary);

            var slot = DonorNameHeuristics.PieceTypeFromStem(baseStem) ?? "Other";
            var gender = DonorNameHeuristics.GenderFrom(primaryStem, primary) ?? Gender.Unisex;
            var weight = DonorNameHeuristics.WeightFromPath(primary);

            pieces.Add(new PieceBuild(baseStem, slot, primary, gender, weight));
        }

        var variants = pieces
            .GroupBy(p => (p.Gender, p.Weight))
            .OrderBy(g => g.Key.Gender)
            .ThenBy(g => g.Key.Weight)
            .Select(g => ToVariant(group.Id, g.Key.Gender, g.Key.Weight, g, textureMap))
            .ToList();

        return variants;
    }

    private static Variant ToVariant(
        string setId,
        Gender gender,
        WeightClass weight,
        IEnumerable<PieceBuild> pieceBuilds,
        IReadOnlyDictionary<string, TextureGroup> textureMap)
    {
        var hasTextures = textureMap.TryGetValue(setId, out var textures);
        var pieces = pieceBuilds
            .OrderBy(p => p.Slot, StringComparer.Ordinal)
            .ThenBy(p => p.EditorId, StringComparer.Ordinal)
            .ThenBy(p => p.MeshPath, StringComparer.Ordinal)
            .Select(p => new Piece(
                p.EditorId,
                0,
                p.Slot,
                null,
                p.MeshPath,
                hasTextures ? textures!.PathsFor(p.Slot) : Array.Empty<string>()))
            .ToList();

        return new Variant(gender, weight, pieces);
    }

    private static IReadOnlyDictionary<string, TextureGroup> IndexTexturesByKey(IReadOnlyList<string> texturePaths)
    {
        var groups = new SortedDictionary<string, TextureGroup>(StringComparer.Ordinal);
        foreach (var texture in texturePaths)
        {
            var key = KeyNormalizer.NormalizeMeshFolder(texture);
            if (key is null)
            {
                continue;
            }

            if (!groups.TryGetValue(key.Id, out var group))
            {
                group = new TextureGroup();
                groups.Add(key.Id, group);
            }

            var type = DonorNameHeuristics.PieceTypeFromStem(Path.GetFileNameWithoutExtension(texture));
            if (type is not null)
            {
                group.Add(type, texture);
            }
        }

        return groups;
    }

    private sealed class SetBuild(string id, string displayName)
    {
        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public List<string> Meshes { get; } = new();
    }

    private sealed record PieceBuild(string EditorId, string Slot, string MeshPath, Gender Gender, WeightClass Weight);

    private sealed class TextureGroup
    {
        private readonly SortedDictionary<string, SortedSet<string>> _byType = new(StringComparer.Ordinal);

        public void Add(string type, string path)
        {
            if (!_byType.TryGetValue(type, out var paths))
            {
                paths = new SortedSet<string>(StringComparer.Ordinal);
                _byType.Add(type, paths);
            }

            paths.Add(path);
        }

        public IReadOnlyList<string> PathsFor(string type)
        {
            return _byType.TryGetValue(type, out var paths) ? paths.ToList().AsReadOnly() : Array.Empty<string>();
        }
    }
}