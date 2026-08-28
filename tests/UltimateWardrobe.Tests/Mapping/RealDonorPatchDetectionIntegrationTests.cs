using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.DonorLibrary;
using UltimateWardrobe.Mapping;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.Mapping;

/// <summary>
/// Sprint 3.4.2 - real-donor integration spot-check for the mapping layer, gated behind the
/// <c>Integration</c> category and auto-skipped (with an output note) whenever
/// <c>ModsForTests/Armor</c> has no "Red Hood - HIMBO" archive (the <see cref="RealDonorIntegrationTests"/>
/// pattern carried into the Mapping project). Red Hood - HIMBO is the esp-less branch-2 fixture that
/// classifies as <see cref="DonorAssetKind.BodyConversionPatch"/> with real BodySlide + physics flags
/// (recorded in <c>Docs/donor-library.md</c>: 1 set, 2 SliderSets, 10 physics flags). Here those REAL
/// flags are used to drive the mapping <see cref="MappingService.NeedFor"/>/<see cref="MappingService.GetStatus"/>:
/// under <see cref="PatchPolicy.RequireBodyConversion"/> / <see cref="PatchPolicy.RequirePhysics"/> a piece
/// mapped to this donor must read <see cref="PatchRequirement.None"/> / <see cref="MappingStatus.Mapped"/>,
/// proving the mapping status reacts to the real classifier output (it would read NeedsPatch if the real
/// donor carried no such flags). Cleaned up on every path.
/// </summary>
[Trait("Category", "Integration")]
public class RealDonorPatchDetectionIntegrationTests
{
    private const string GameRoot = @"D:\Skymod\Stock Game";

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

    public RealDonorPatchDetectionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Real_BodySlide_And_Physics_Flags_Drive_Mapping_Status_To_Mapped()
    {
        var archive = FindArmorArchive("Red Hood - HIMBO");
        if (archive is null || !File.Exists(archive))
        {
            _output.WriteLine("Skipped: no 'Red Hood - HIMBO' archive under ModsForTests/Armor.");
            return;
        }

        // Classify the real donor with the vanilla hint (same pipeline as the Phase 2 spot-check).
        var dest = Path.Combine(Path.GetTempPath(), "UW_Donor_Map_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);

        DonorAsset asset;
        var imported = await new DonorImportService().ImportAsync(archive, dest);
        var hint = Directory.Exists(GameRoot)
            ? new Catalog(new VanillaCatalogSource(GameRoot), Array.Empty<ArmorSet>())
            : null;
        asset = await new DonorClassifier().ClassifyAsync(imported.ExtractedPath, hint);

        try
        {
            _output.WriteLine(
                $"[mapping-real] {Path.GetFileName(archive)} -> Kind={asset.Kind} | bodySlide={asset.DetectedBodySlideFiles.Count}" +
                $" physics={asset.DetectedPhysicsFiles.Count} | sets={asset.ProvidedSets.Count}");

            // Loose asserts - the real donor must carry the body/physics flags the spot-check depends on.
            Assert.True(asset.ProvidedSets.Count >= 1, "Red Hood - HIMBO produced no provided set.");
            Assert.NotEmpty(asset.DetectedBodySlideFiles);
            Assert.NotEmpty(asset.DetectedPhysicsFiles);

            // A synthetic target catalog + a mapping bound to the REAL donor's flags.
            var catalog = SyntheticCatalogUniverse.CreateIronCatalog();
            var project = new Project(Guid.NewGuid(), "Integration", "C:/Projects/Integration");
            var overhaul = new Overhaul(Guid.NewGuid(), "SpotCheck", project.Id, catalog.Source);
            project.Library.Assets.Add(asset);
            var service = new MappingService(project.Library);

            var donorPiece = PickBodyBiasedDonorPiece(asset);
            var targetPiece = catalog.Sets[0].Variants.First(v => v.Gender == Gender.Male)
                .Pieces.First(p => p.EditorId == "ArmorIronCuirass");

            var mapping = new PieceMapping(
                Guid.NewGuid(), overhaul.Id, catalog.Sets[0].Id, targetPiece.EditorId, Gender.Male,
                asset.ImportId, donorPiece.EditorId, donorPiece.MeshPath ?? "meshes/redhood/a.nif",
                status: MappingStatus.Mapped);

            // RequireBodyConversion: the real BodySlide flag satisfies the body layer -> None.
            var bodyNeed = service.NeedFor(mapping, asset, policy: PatchPolicy.RequireBodyConversion);
            // RequirePhysics: the real physics flag satisfies the physics layer -> None.
            var physicsNeed = service.NeedFor(mapping, asset, policy: PatchPolicy.RequirePhysics);
            // RequireBoth: neither layer missing -> Mapped. If the donor carried no real flags these
            // would be Body/Physics/Both or NeedsPatch - the reaction to the real flags is the assertion.
            var bothStatus = service.GetStatus(mapping, asset, policy: PatchPolicy.RequireBoth);

            _output.WriteLine(
                $"[mapping-real] RequireBodyConversion={bodyNeed} RequirePhysics={physicsNeed} RequireBoth={bothStatus} (cuirass {donorPiece.EditorId}, slot {donorPiece.Slot})");

            Assert.Equal(PatchRequirement.None, bodyNeed);
            Assert.Equal(PatchRequirement.None, physicsNeed);
            Assert.Equal(MappingStatus.Mapped, bothStatus);

        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }
    }

    /// <summary>
    /// Prefers a body-slot ("32"-prefixed) donor piece so the RequireBodyConversion branch of
    /// <see cref="MappingService.NeedFor"/> evaluates a real body piece; falls back to the first piece.
    /// </summary>
    private static Piece PickBodyBiasedDonorPiece(DonorAsset asset)
    {
        foreach (var set in asset.ProvidedSets)
        {
            var body = set.Variants
                .SelectMany(v => v.Pieces)
                .FirstOrDefault(p => p.Slot.StartsWith("32", StringComparison.Ordinal));
            if (body is not null)
            {
                return body;
            }

            var any = set.Variants.SelectMany(v => v.Pieces).FirstOrDefault();
            if (any is not null)
            {
                return any;
            }
        }

        throw new InvalidOperationException("Red Hood - HIMBO yielded no donor piece to map.");
    }
}
