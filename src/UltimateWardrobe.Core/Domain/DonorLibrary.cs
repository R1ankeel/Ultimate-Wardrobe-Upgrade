namespace UltimateWardrobe.Core.Domain;

public sealed class DonorLibrary
{
    public Guid ProjectId { get; }
    public List<DonorAsset> Assets { get; }

    public DonorLibrary(Guid projectId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        ProjectId = projectId;
        Assets = new List<DonorAsset>();
    }
}
