using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Core.Abstractions;

public interface IDonorClassifier
{
    Task<DonorAsset> ClassifyAsync(string extractedDir, Catalog? catalogHint = null, CancellationToken cancellationToken = default);
}
