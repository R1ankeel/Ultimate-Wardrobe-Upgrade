namespace UltimateWardrobe.Core.Domain;

public sealed class Piece
{
    public string EditorId { get; init; }
    public uint FormId { get; init; }
    public string Slot { get; init; }
    public string? ArmaEditorId { get; init; }
    public string? MeshPath { get; init; }
    public IReadOnlyList<string> TexturePaths { get; init; }

    public Piece(string editorId, uint formId, string slot, string? armaEditorId = null, string? meshPath = null, IReadOnlyList<string>? texturePaths = null)
    {
        if (string.IsNullOrWhiteSpace(editorId)) throw new ArgumentException("EditorId must not be empty.", nameof(editorId));
        if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("Slot must not be empty.", nameof(slot));

        EditorId = editorId;
        FormId = formId;
        Slot = slot;
        ArmaEditorId = armaEditorId;
        MeshPath = meshPath;
        TexturePaths = texturePaths ?? Array.Empty<string>();
    }
}
