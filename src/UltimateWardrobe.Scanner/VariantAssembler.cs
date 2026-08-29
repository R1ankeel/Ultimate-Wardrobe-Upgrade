using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Scanner;

/// <summary>
/// Assembles the grouping output (Sprint 1.3) into <see cref="Variant"/>s per
/// (Gender, Weight) (Sprint 1.4.3): one <see cref="Variant"/> per (Gender, Weight) pair present
/// in the set. The same ARMO may yield two <see cref="Piece"/>s (same EditorId, different
/// gender), matching <see cref="PieceMapping.UniqueKey"/> semantics. Pieces are assigned the
/// frozen "{number} {Name}" slot string via <see cref="BipedSlotMapper"/> and pieces are ordered
/// by slot then EditorId (1.4.4).
/// </summary>
public static class VariantAssembler
{
    public static IReadOnlyList<ArmorSet> Assemble(
        GroupingResult grouping,
        RecordIndex index,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        var sets = new List<ArmorSet>();

        foreach (var grouped in grouping.Sets.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variants = AssembleVariants(grouped.Members, index, warnings);
            sets.Add(new ArmorSet(grouped.Id, grouped.DisplayName, variants));
        }

        return sets;
    }

    private static IReadOnlyList<Variant> AssembleVariants(
        IReadOnlyList<CorrelatedArmor> members,
        RecordIndex index,
        List<ScanWarning> warnings)
    {
        var piecesByVariant = new Dictionary<(Gender, WeightClass), List<(int SlotIndex, Piece Piece)>>();
        var order = new List<(Gender, WeightClass)>();

        foreach (var member in members)
        {
            var genders = ScanReportBuilder.Guard("detecting gender", member.EditorId, () => GenderWeightDetector.DetectGenders(member, index, warnings));
            var weight = ScanReportBuilder.Guard("detecting weight", member.EditorId, () => GenderWeightDetector.DetectWeight(member, index));
            var slot = BipedSlotMapper.ToSlotString(member.BipedFlags)
                       ?? $"BODT {(uint)member.BipedFlags}";
            var slotIndex = BipedSlotMapper.SlotIndex(member.BipedFlags);

            foreach (var gender in genders)
            {
                var key = (gender, weight);
                if (!piecesByVariant.TryGetValue(key, out var pieces))
                {
                    pieces = new List<(int SlotIndex, Piece Piece)>();
                    piecesByVariant[key] = pieces;
                    order.Add(key);
                }

                // F2: per-gender mesh and ARMA id (male mesh for Male variant, female mesh for Female)
                string? meshPath;
                string? armaEditorId;
                if (gender == Gender.Male)
                {
                    meshPath = member.MeshPathMale ?? member.MeshPath;
                    armaEditorId = member.ArmaEditorIdMale ?? member.ArmaEditorId;
                }
                else if (gender == Gender.Female)
                {
                    meshPath = member.MeshPathFemale ?? member.MeshPath;
                    armaEditorId = member.ArmaEditorIdFemale ?? member.ArmaEditorId;
                }
                else
                {
                    // Unisex - use whatever is available (male preferred, then female)
                    meshPath = member.MeshPathMale ?? member.MeshPathFemale ?? member.MeshPath;
                    armaEditorId = member.ArmaEditorId;
                }

                // Fallback: if per-gender mesh is still null but ARMA has opposite gender mesh, reuse available
                meshPath ??= member.MeshPath;

                pieces.Add((slotIndex, new Piece(
                    member.EditorId,
                    member.FormId,
                    slot,
                    armaEditorId,
                    meshPath,
                    member.TexturePaths)));
            }
        }

        var variants = new List<Variant>(order.Count);
        foreach (var key in order)
        {
            var pieces = piecesByVariant[key]
                .OrderBy(p => p.SlotIndex)
                .ThenBy(p => p.Piece.EditorId, StringComparer.Ordinal)
                .Select(p => p.Piece)
                .ToList();
            variants.Add(new Variant(key.Item1, key.Item2, pieces));
        }

        return variants;
    }
}