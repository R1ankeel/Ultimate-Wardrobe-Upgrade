namespace UltimateWardrobe.Core.Domain;

public sealed class Project
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string RootPath { get; init; }
    public DonorLibrary Library { get; }
    public List<Overhaul> Overhauls { get; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public int SchemaVersion { get; init; }

    public Project(Guid id, string name, string rootPath, int schemaVersion = 1)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name must not be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("RootPath must not be empty.", nameof(rootPath));

        Id = id;
        Name = name;
        RootPath = rootPath;
        SchemaVersion = schemaVersion;
        CreatedAt = DateTime.UtcNow;
        ModifiedAt = CreatedAt;
        Library = new DonorLibrary(id);
        Overhauls = new List<Overhaul>();
    }

    public void Touch()
    {
        // Mutable timestamp via reflection-like helper is not needed for Phase 0.
        // Callers can set ModifiedAt via with-expression if record-style is needed.
        // Kept as method for future persistence timestamp bumps.
    }
}
