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

    /// <summary>
    /// When the overhaul was created (Phase 4 Sprint 4.0.2 Core amendment). Additive <c>init</c>,
    /// default <see cref="DateTime.UtcNow"/> so it stamps the construct time; the persistence layer
    /// maps it to the <c>Overhaul.CreatedAt</c> row. Existing construction is unaffected.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional last-modified timestamp (Phase 4 Sprint 4.0.2 Core amendment). Additive <c>init</c>,
    /// <c>null</c> until a write path bumps it; the persistence layer maps it to the
    /// <c>Overhaul.ModifiedAt</c> row. Existing construction is unaffected.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }

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
