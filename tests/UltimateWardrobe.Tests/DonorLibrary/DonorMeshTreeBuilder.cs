namespace UltimateWardrobe.Tests.DonorLibrary;

/// <summary>
/// Sprint 2.2 branch-2 fixtures: writes runtime-synthesized mesh/texture trees (plain empty
/// files) in root or <c>Data/</c> layout. No Mutagen needed - branch 2 must never depend on an
/// esp.
/// </summary>
internal static class DonorMeshTreeBuilder
{
    public static void Write(string root, params string[] gameRelativePaths)
    {
        foreach (var path in gameRelativePaths)
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var _ = File.Create(full);
        }
    }
}