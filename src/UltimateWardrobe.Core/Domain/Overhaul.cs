using UltimateWardrobe.Core.Enums;

namespace UltimateWardrobe.Core.Domain;

public sealed class Overhaul
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public Guid ProjectId { get; init; }
    public CatalogSource Source { get; init; }
    public List<PieceMapping> Mappings { get; }

    /// <summary>
    /// The target body / physics demand (roadmap 5.3). Defaults to <see cref="PatchPolicy.Loose"/>,
    /// so the donor's own <c>Detected*</c> flags drive <c>NeedsPatch</c>; the Phase 6 UI sets the
    /// stricter values. Additive - existing constructor calls and object initializers are unaffected.
    /// </summary>
    public PatchPolicy Policy { get; init; } = PatchPolicy.Loose;

    public Overhaul(Guid id, string name, Guid projectId, CatalogSource source)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name must not be empty.", nameof(name));
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        Source = source ?? throw new ArgumentNullException(nameof(source));

        Id = id;
        Name = name;
        ProjectId = projectId;
        Mappings = new List<PieceMapping>();
    }
}
