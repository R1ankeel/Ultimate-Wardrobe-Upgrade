using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Core.Abstractions;

public interface ICatalogScanner
{
    Task<Catalog> ScanAsync(CatalogSource source, CancellationToken cancellationToken = default);
}
