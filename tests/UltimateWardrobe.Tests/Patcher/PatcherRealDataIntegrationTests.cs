using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Patcher;
using UltimateWardrobe.Scanner;
using UltimateWardrobe.Tests.Persistence;
using UltimateWardrobe.Tests.Scanner;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.Patcher;

/// <summary>
/// Sprint 5.4.1 - Integration-gated real-data patcher spot-check, gated behind the
/// <c>Integration</c> category and auto-skipped (with an output note) whenever the Skyrim game root
/// or <c>ModsForTests/Armor</c> is absent (the <see cref="RealDonorIntegrationTests"/> pattern).
/// Imports + classifies a REAL esp-bearing donor ("Red Hood - Main File", branch 1, so the provided
/// sets carry real <see cref="Piece.TexturePaths"/>), scans the REAL game for the vanilla Iron set,
/// maps one real Iron piece to the donor's first physically-resolvable piece, runs the full
/// <see cref="WardrobePatcher"/> pipeline, then asserts: the generated esp re-opens under Mutagen
/// (masters include <c>Skyrim.esm</c>, ESL gate on for the Vanilla source), the overridden ARMA's
/// WorldModel path equals the donor's game-relative mesh path and PHYSICALLY resolves under the
/// extracted <c>Source/&lt;ImportId&gt;/</c> folder (root or Data layout), and the mod folder
/// contains exactly the reported sliced files + the esp + <c>meta.ini</c> - nothing else. The donor
/// folder + the test output dir are cleaned in <c>finally</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PatcherRealDataIntegrationTests
{
    private const string GameRoot = @"D:\Skymod\Stock Game";
    private const string FixtureNamePart = "Red Hood - Main File";
    private const string OverhaulName = "RealIronSpotCheck";

    private static string ArmorDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ModsForTests", "Armor"));

    private static string? FindArmorArchive(string namePart)
    {
        if (!Directory.Exists(ArmorDir))
        {
            return null;
        }

        return Directory.EnumerateFiles(ArmorDir)
            .FirstOrDefault(f => Path.GetFileName(f).Contains(namePart, StringComparison.OrdinalIgnoreCase));
    }

    private readonly ITestOutputHelper _output;

    public PatcherRealDataIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RealDonor_RealGame_GeneratesMutagenValidModFolder()
    {
        if (!Directory.Exists(GameRoot))
        {
            _output.WriteLine($"Skipped: Skyrim game root '{GameRoot}' is absent.");
            return;
        }

        var archive = FindArmorArchive(FixtureNamePart);
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine($"Skipped: no '{FixtureNamePart}' archive under ModsForTests/Armor.");
            return;
        }

        // 1. A REAL game catalog: the vanilla Iron set with its real piece editor IDs/ARMA names.
        var catalog = await new FolderCatalogScanner().ScanAsync(new VanillaCatalogSource(GameRoot));
        _output.WriteLine(
            $"[patcher-integration] Real game scan: TotalArmo={catalog.Stats.TotalArmo}, GroupedSets={catalog.Stats.GroupedSets}, Warnings={catalog.Warnings.Count}");

        var ironSet = Assert.Single(
            catalog.Sets,
            s => s.Variants.SelectMany(v => v.Pieces).Any(p => p.EditorId == "ArmorIronCuirass"));
        // F1 fix: iron now correctly has Male and Female variants (both contain the cuirass). Pick Male deterministically if present, otherwise first.
        var ironVariant = ironSet.Variants.FirstOrDefault(v => v.Gender == Gender.Male && v.Pieces.Any(p => p.EditorId == "ArmorIronCuirass"))
                          ?? ironSet.Variants.First(v => v.Pieces.Any(p => p.EditorId == "ArmorIronCuirass"));
        var targetGender = ironVariant.Gender;
        _output.WriteLine($"[patcher-integration] Iron set '{ironSet.Id}': Variant gender={targetGender}, Pieces={string.Join(",", ironVariant.Pieces.Select(p => p.EditorId))}");

        // 2. Import + classify the REAL esp-bearing donor (branch 1 preferred for real TexturePaths).
        var donorDest = Path.Combine(Path.GetTempPath(), "UW_Patcher_Donor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(donorDest);
        using var dir = new TestTempDir();

        try
        {
            var imported = await new DonorImportService().ImportAsync(archive, donorDest);
            var hint = new Catalog(new VanillaCatalogSource(GameRoot), Array.Empty<ArmorSet>());
            var donor = await new UltimateWardrobe.DonorLibrary.DonorClassifier().ClassifyAsync(imported.ExtractedPath, hint);

            _output.WriteLine(
                $"[patcher-integration] {Path.GetFileName(archive)} -> Kind={donor.Kind} | sets={donor.ProvidedSets.Count}" +
                $" | bodySlide={donor.DetectedBodySlideFiles.Count} physics={donor.DetectedPhysicsFiles.Count}" +
                $" | files={donor.FileManifest.Count} | path={donor.ExtractedPath}");

            Assert.NotEmpty(donor.FileManifest);
            Assert.NotEqual(DonorAssetKind.Unknown, donor.Kind);
            Assert.NotEmpty(donor.ProvidedSets);
            foreach (var providedSet in donor.ProvidedSets)
            {
                foreach (var variant in providedSet.Variants)
                {
                    foreach (var piece in variant.Pieces)
                    {
                        var mesh = piece.MeshPath is null ? "<null>" : piece.MeshPath;
                        var located = piece.MeshPath is not null
                            && new DonorFileLocator(donor.ExtractedPath).TryLocate(piece.MeshPath) is not null;
                        _output.WriteLine($"[patcher-integration]   provided '{providedSet.Id}' {variant.Gender}: piece '{piece.EditorId}' mesh='{mesh}' located={located} textures={piece.TexturePaths.Count}");
                    }
                }
            }

            // 3. The mapped donor mesh: a game-relative path that PHYSICALLY exists under the
            //    extracted folder (root or Data layout) - the seller of the "real donor file"
            //    assertion below. The Red Hood esp references meshes without the meshes/ prefix
            //    ("armor/ZerofrostRedHood/M/..."), so its provided pieces do not resolve; the real
            //    archive files do, and the mapping is pointed at the correct physical file.
            var providedPieces = donor.ProvidedSets
                .SelectMany(s => s.Variants)
                .SelectMany(v => v.Pieces)
                .ToList();
            var meshCandidates = new List<string>();
            foreach (var candidate in providedPieces)
            {
                if (candidate.MeshPath is not null)
                {
                    meshCandidates.Add(PatchPathRules.Normalize(candidate.MeshPath));
                }
            }

            var donorMesh = meshCandidates.FirstOrDefault(
                p => new DonorFileLocator(donor.ExtractedPath).TryLocate(p) is not null);

            if (donorMesh is null)
            {
                donorMesh = donor.FileManifest
                    .Select(e => PatchPathRules.ToGameRelative(e.RelativePath))
                    .Where(p => p.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)
                        && p.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(p => new DonorFileLocator(donor.ExtractedPath).TryLocate(p) is not null);
                _output.WriteLine("[patcher-integration] Donor esp referenced no physically-present mesh; " +
                    "using the first real 'meshes/**/*.nif' entry from the archive.");
            }

            Assert.NotNull(donorMesh);
            Assert.NotNull(new DonorFileLocator(donor.ExtractedPath).TryLocate(donorMesh));
            var donorPieceEditorId = providedPieces
                    .FirstOrDefault(p => PatchPathRules.EqualsNormalized(
                        p.MeshPath is null ? string.Empty : PatchPathRules.Normalize(p.MeshPath), donorMesh))
                    ?.EditorId
                ?? Path.GetFileNameWithoutExtension(donorMesh);
            _output.WriteLine($"[patcher-integration] Mapped donor mesh '{donorMesh}' (donor piece editor '{donorPieceEditorId}'), physical file found.");

            // 4. The overhaul: one REAL map (Iron piece <- donor mesh) over the REAL scanned catalog.
            var overhaul = new Overhaul(Guid.NewGuid(), OverhaulName, Guid.NewGuid(), catalog.Source) { Catalog = catalog };
            overhaul.Mappings.Add(new PieceMapping(
                Guid.NewGuid(), overhaul.Id, ironSet.Id, "ArmorIronCuirass", targetGender,
                donor.ImportId, donorPieceEditorId, donorMesh));

            var library = new UltimateWardrobe.Core.Domain.DonorLibrary(Guid.NewGuid());
            library.Assets.Add(donor);

            var outputDir = Path.Combine(dir.Root, "Output");
            var result = await new WardrobePatcher().BuildAsync(overhaul, library, outputDir);

            // 5. The generated esp re-opens under Mutagen: ESL gate on (Vanilla source), masters
            //    include Skyrim.esm, one UW_-prefixed ARMA override whose WorldModel slot carries the
            //    donor mesh path - and that path PHYSICALLY resolves in the extracted donor folder.
            using var reopened = SkyrimMod.CreateFromBinaryOverlay(result.PluginPath, SkyrimRelease.SkyrimSE);
            Assert.True(reopened.IsSmallMaster, "Vanilla+DLC source esp must carry the light-plugin flag (amendment #7).");
            var masters = reopened.MasterReferences.Select(m => m.Master.FileName.ToString()).ToList();
            Assert.Equal(new[] { "Skyrim.esm" }, masters);

            var uwAddons = reopened.ArmorAddons.Where(r => r.EditorID is { } e && e.StartsWith("UW_", StringComparison.Ordinal)).ToList();
            var overrideAddon = Assert.Single(uwAddons);

            var malePath = overrideAddon.WorldModel?.Male?.File?.GivenPath;
            var femalePath = overrideAddon.WorldModel?.Female?.File?.GivenPath;
            if (targetGender == Gender.Unisex)
            {
                Assert.True(PathsEqual(malePath, donorMesh), $"Unisex male slot '{malePath}' != donor mesh '{donorMesh}'.");
                Assert.True(PathsEqual(femalePath, donorMesh), $"Unisex female slot '{femalePath}' != donor mesh '{donorMesh}'.");
            }
            else
            {
                var written = targetGender == Gender.Male ? malePath : femalePath;
                Assert.True(PathsEqual(written, donorMesh), $"Written slot '{written}' != donor mesh '{donorMesh}'.");
            }

            Assert.NotNull(new DonorFileLocator(donor.ExtractedPath).TryLocate(malePath ?? femalePath));

            // 6. The mod folder holds exactly the reported sliced files + the esp + meta.ini.
            var exportDir = Path.Combine(outputDir, OutputFolder.ModName(overhaul.Name));
            var expectedTree = result.CopiedFiles
                .Concat(new[] { OutputFolder.PluginFileName(overhaul.Name), "meta.ini" })
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedTree, OutputTree(exportDir));
            Assert.True(File.Exists(result.PluginPath));

            // 7. The report + meta.ini.
            var report = result.Report!;
            Assert.Equal(1, report.TotalMappings);
            Assert.Equal(1, report.ResolvedMappings);
            Assert.Equal(0, report.SkippedMappings);
            Assert.Equal(1, report.OverriddenRecords);
            Assert.Equal(result.CopiedFiles, report.CopiedFiles);
            Assert.True(report.CopiedBytes > 0);
            _output.WriteLine($"[patcher-integration] Report: {report.CopiedFiles.Count} files / {report.CopiedBytes} bytes, warnings={report.Warnings.Count}");
            foreach (var warning in report.Warnings)
            {
                _output.WriteLine($"[patcher-integration]   warning: {warning.Message}");
            }

            var meta = File.ReadAllText(Path.Combine(exportDir, "meta.ini"));
            Assert.Contains("[General]", meta, StringComparison.Ordinal);
            Assert.Contains($"name={OutputFolder.ModName(overhaul.Name)}", meta, StringComparison.Ordinal);
            Assert.Contains("version=1.0.0", meta, StringComparison.Ordinal);
            Assert.Contains("category=Armor Replacer", meta, StringComparison.Ordinal);
            Assert.Contains("1 sets mapped.", meta, StringComparison.Ordinal);
            Assert.Matches(
                @"generated=\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC",
                File.ReadAllLines(Path.Combine(exportDir, "meta.ini")).Single(l => l.StartsWith("generated=", StringComparison.Ordinal)));
        }
        finally
        {
            TestHelpers.DeleteDirectoryRetry(donorDest);
        }
    }

    private static IReadOnlyList<string> OutputTree(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool PathsEqual(string? a, string b)
    {
        return a is not null && PatchPathRules.EqualsNormalized(a, b);
    }
}