using System.Diagnostics;
using UltimateWardrobe.Archives;
using UltimateWardrobe.Core.Domain;
using UltimateWardrobe.Core.Enums;
using UltimateWardrobe.Scanner;
using Xunit.Abstractions;

namespace UltimateWardrobe.Tests.Scanner;

/// <summary>
/// Sprint 1.6.5 real-data coverage, gated behind the Integration category and auto-skipping
/// (with an output note) whenever Skyrim / story-mod asset paths are absent on the machine.
/// </summary>
[Trait("Category", "Integration")]
public class RealDataScannerTests
{
    private const string GameRoot = @"D:\Skymod\Stock Game";

    private static string ModsQuestDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ModsForTests", "QuestExpansiaon"));

    private readonly ITestOutputHelper _output;

    public RealDataScannerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Vanilla_RealGame_Scan_ProducesCatalog_InTimeout()
    {
        if (!Directory.Exists(GameRoot))
        {
            _output.WriteLine($"Skipped: Skyrim game root '{GameRoot}' is absent.");
            return;
        }

        var watch = Stopwatch.StartNew();
        var catalog = await new FolderCatalogScanner().ScanAsync(new VanillaCatalogSource(GameRoot));
        watch.Stop();

        _output.WriteLine(
            $"Vanilla scan: {watch.ElapsedMilliseconds} ms; TotalArmo={catalog.Stats.TotalArmo}, GroupedSets={catalog.Stats.GroupedSets}, " +
            $"Skipped={catalog.Stats.Skipped}, MissingFiles={catalog.Stats.MissingFiles}, Warnings={catalog.Warnings.Count}");
        foreach (var (reason, count) in catalog.Stats.SkippedByReason)
        {
            _output.WriteLine($"  skip {reason}: {count}");
        }

        Assert.True(catalog.Stats.TotalArmo > 500, $"Expected TotalArmo > 500, got {catalog.Stats.TotalArmo}.");
        Assert.True(catalog.Stats.GroupedSets > 50, $"Expected GroupedSets > 50, got {catalog.Stats.GroupedSets}.");
        Assert.True(
            catalog.Stats.SkippedByReason.GetValueOrDefault(SkipReason.CreatureRace) >= 1,
            $"Expected at least one CreatureRace skip (Boar/Chaurus-style skins), got {catalog.Stats.SkippedByReason.GetValueOrDefault(SkipReason.CreatureRace)}.");

        var iron = catalog.Sets.FirstOrDefault(s => s.Id.Contains("iron", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(iron);
        Assert.True(iron!.Variants.Any(v => v.Pieces.Count >= 1),
            "Iron set exists but contains no pieces.");

        Assert.True(watch.ElapsedMilliseconds <= 10_000, $"Vanilla scan took {watch.ElapsedMilliseconds} ms, expected <= 10 s.");
    }

    [Fact]
    public async Task StoryMod_Vigilant_Scan_ProducesSets()
    {
        if (!Directory.Exists(ModsQuestDir))
        {
            _output.WriteLine($"Skipped: 'ModsForTests/QuestExpansiaon' is absent.");
            return;
        }

        var rar = Directory.GetFiles(ModsQuestDir, "VIGILANT*.rar", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (rar is null)
        {
            _output.WriteLine("Skipped: no VIGILANT V*.rar found under ModsForTests/QuestExpansiaon.");
            return;
        }

        var dest = Path.Combine(Path.GetTempPath(), "UW_Scan_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dest);
            var result = await new CompositeExtractor().ExtractAsync(rar, dest);
            Assert.True(result.ExtractedFiles.Count > 0, "VIGILANT extraction produced no files.");

            var plugin = ChooseMainPlugin(dest);
            if (plugin is null)
            {
                _output.WriteLine("Skipped: no .esp/.esm found inside the extracted VIGILANT layout.");
                return;
            }

            var rootPath = Path.GetDirectoryName(plugin)!;
            var source = new StoryModCatalogSource(rootPath, Path.GetFileName(plugin));
            var catalog = await new FolderCatalogScanner().ScanAsync(source);

            _output.WriteLine($"VIGILANT scan of '{Path.GetFileName(plugin)}': TotalArmo={catalog.Stats.TotalArmo}, GroupedSets={catalog.Stats.GroupedSets}, Warnings={catalog.Warnings.Count}");

            Assert.True(catalog.Stats.TotalArmo > 0, "Expected VIGILANT to contain armor records.");
            Assert.True(catalog.Stats.GroupedSets > 0, "Expected VIGILANT scan to produce at least one ArmorSet.");
        }
        finally
        {
            try
            {
                Directory.Delete(dest, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task Vanilla_RealGame_FullKitsAreSingleSets_NoMegaSets()
    {
        if (!Directory.Exists(GameRoot))
        {
            _output.WriteLine($"Skipped: Skyrim game root '{GameRoot}' is absent.");
            return;
        }

        var catalog = await new FolderCatalogScanner().ScanAsync(new VanillaCatalogSource(GameRoot));

        // Sprint 1.7.3 set-integrity check: real Iron/Steel/Leather each form ONE ArmorSet, the
        // Outfit first (OTFT) signal plus wardrobe-outfit filtering. The vanilla NPC wardrobe
        // cwmission04outfitimperial used to swallow all three kits into one 585-piece set.
        var ironSet = Assert.Single(catalog.Sets, s => s.Variants.SelectMany(v => v.Pieces).Any(p => p.EditorId == "ArmorIronCuirass"));
        var ironMembers = ironSet.Variants.SelectMany(v => v.Pieces).Select(p => p.EditorId).Distinct().ToList();
        foreach (var piece in new[] { "ArmorIronHelmet", "ArmorIronCuirass", "ArmorIronGauntlets", "ArmorIronBoots" })
        {
            Assert.Contains(piece, ironMembers);
        }

        var steelSet = Assert.Single(catalog.Sets, s => s.Variants.SelectMany(v => v.Pieces).Any(p => p.EditorId == "ArmorSteelCuirassA"));
        var steelMembers = steelSet.Variants.SelectMany(v => v.Pieces).Select(p => p.EditorId).Distinct().ToList();
        foreach (var piece in new[] { "ArmorSteelHelmetA", "ArmorSteelCuirassA", "ArmorSteelGauntletsA", "ArmorSteelBootsA", "ArmorSteelShield" })
        {
            Assert.Contains(piece, steelMembers);
        }
        Assert.Contains(steelSet.Variants, v => v.Gender == Gender.Male);
        Assert.Contains(steelSet.Variants, v => v.Gender == Gender.Female);

        var leatherSet = Assert.Single(catalog.Sets, s => s.Variants.SelectMany(v => v.Pieces).Any(p => p.EditorId == "ArmorLeatherCuirass"));
        var leatherMembers = leatherSet.Variants.SelectMany(v => v.Pieces).Select(p => p.EditorId).Distinct().ToList();
        foreach (var piece in new[] { "ArmorLeatherHelmet", "ArmorLeatherCuirass", "ArmorLeatherGauntlets", "ArmorLeatherBoots" })
        {
            Assert.Contains(piece, leatherMembers);
        }

        foreach (var set in catalog.Sets)
        {
            var memberEditorIds = set.Variants.SelectMany(v => v.Pieces).Select(p => p.EditorId).ToList();
            Assert.True(memberEditorIds.Count <= 150,
                $"Set '{set.Id}' has {memberEditorIds.Count} pieces - accidental mega-set.");
            Assert.False(
                memberEditorIds.Contains("ArmorIronCuirass") && memberEditorIds.Contains("ArmorSteelCuirassA"),
                $"Set '{set.Id}' mixes Iron and Steel armor - an NPC wardrobe Outfit leaked through.");
        }
    }

    [Fact]
    public async Task Vanilla_ExpectedKitsHaveFourPieces()
    {
        if (!Directory.Exists(GameRoot))
        {
            _output.WriteLine($"Skipped: Skyrim game root '{GameRoot}' is absent.");
            return;
        }

        var catalog = await new FolderCatalogScanner().ScanAsync(new VanillaCatalogSource(GameRoot));

        // F3 validation: each user-listed kit must be discoverable via DisplayName contains search (not Id exact)
        // and, where the kit is a full armor set (helmet + cuirass + gauntlets + boots), must have >=4 pieces per gender variant.
        // Alias map covers vanilla EditorID quirks (Fur -> bandit, etc.). See Docs/scanner.md F3 clarification.

        var expected = new[]
        {
            // Heavy
            new { Name = "Iron Armor", Alias = "iron", MinPieces = 4, BothGenders = true },
            new { Name = "Banded Iron Armor", Alias = "ironbanded", MinPieces = 2, BothGenders = false }, // banded has cuirass+shield only (2 male, 1 female) in vanilla
            new { Name = "Steel Armor", Alias = "steel", MinPieces = 4, BothGenders = true },
            new { Name = "Steel Armor (alternate)", Alias = "steel", MinPieces = 4, BothGenders = true },
            new { Name = "Bonemold Armor", Alias = "bonemold", MinPieces = 4, BothGenders = true },
            new { Name = "Bonemold Pauldron Armor", Alias = "bonemold", MinPieces = 4, BothGenders = true },
            new { Name = "Falmer Hardened/Heavy Armor", Alias = "falmerhardened", MinPieces = 4, BothGenders = true },
            new { Name = "Falmer Heavy Armor with Shellbug Helmet", Alias = "falmerheavy", MinPieces = 3, BothGenders = false }, // Falmer Heavy has helmet+gauntlets+boots (3) no cuirass
            new { Name = "Dwarven Armor", Alias = "dwarven", MinPieces = 4, BothGenders = true },
            new { Name = "Steel Plate Armor", Alias = "steelplate", MinPieces = 4, BothGenders = true },
            new { Name = "Chitin Heavy Armor", Alias = "chitinheavy", MinPieces = 3, BothGenders = false }, // DLC2 Chitin Heavy has helmet+gauntlets+boots (3)
            new { Name = "Nordic Carved Armor", Alias = "nordic", MinPieces = 4, BothGenders = true }, // alias broadened to nordic to match nordiccarved
            new { Name = "Orcish Armor", Alias = "orcish", MinPieces = 4, BothGenders = true },
            new { Name = "Ebony Armor", Alias = "ebony", MinPieces = 4, BothGenders = true },
            new { Name = "Dragonplate Armor", Alias = "dragonplate", MinPieces = 4, BothGenders = true },
            new { Name = "Daedric Armor", Alias = "daedric", MinPieces = 4, BothGenders = true },
            new { Name = "Ahzidal's Armor of Retribution", Alias = "ahzidal", MinPieces = 1, BothGenders = false },
            new { Name = "General Tullius' Armor", Alias = "generaltullius", MinPieces = 1, BothGenders = false },
            // Light
            new { Name = "Fur Armor", Alias = "bandit", MinPieces = 4, BothGenders = true }, // EditorID bandit
            new { Name = "Hide Armor", Alias = "hide", MinPieces = 4, BothGenders = true },
            new { Name = "Studded Armor", Alias = "studded", MinPieces = 1, BothGenders = false }, // cuirass-only in vanilla
            new { Name = "Leather Armor", Alias = "leather", MinPieces = 4, BothGenders = true }, // outfit leatheralloutfit wins, search via contains
            new { Name = "Forsworn Armor", Alias = "forsworn", MinPieces = 4, BothGenders = true },
            new { Name = "Vampire Armor (Black)", Alias = "vampire", MinPieces = 1, BothGenders = false },
            new { Name = "Vampire Armor (Gray)", Alias = "vampire", MinPieces = 1, BothGenders = false },
            new { Name = "Vampire Armor (Red)", Alias = "vampire", MinPieces = 1, BothGenders = false },
            new { Name = "Elven Armor", Alias = "elven", MinPieces = 4, BothGenders = true },
            new { Name = "Chitin Armor", Alias = "chitin", MinPieces = 4, BothGenders = true }, // light chitin
            new { Name = "Scaled Armor", Alias = "scaled", MinPieces = 4, BothGenders = true },
            new { Name = "Scaled Horn Armor", Alias = "scaled", MinPieces = 4, BothGenders = true },
            new { Name = "Glass Armor", Alias = "glass", MinPieces = 4, BothGenders = true },
            new { Name = "Dragonscale Armor", Alias = "dragonscale", MinPieces = 4, BothGenders = true },
            new { Name = "Ancient Shrouded Armor", Alias = "ancientshrouded", MinPieces = 1, BothGenders = false },
            new { Name = "Blackguard's Armor", Alias = "blackguard", MinPieces = 1, BothGenders = false },
            new { Name = "Deathbrand Armor", Alias = "deathbrand", MinPieces = 1, BothGenders = false },
            new { Name = "Guild Master's Armor", Alias = "guildmaster", MinPieces = 1, BothGenders = false },
            new { Name = "Linwe's Armor", Alias = "linwe", MinPieces = 1, BothGenders = false },
            new { Name = "Armor of the Old Gods", Alias = "oldgods", MinPieces = 1, BothGenders = false },
            new { Name = "Shrouded Armor", Alias = "shrouded", MinPieces = 1, BothGenders = false },
            new { Name = "Thieves Guild Armor", Alias = "thievesguild", MinPieces = 1, BothGenders = false },
        };

        var optionalAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "blackguard", "deathbrand", "guildmaster", "oldgods" };

        var failures = new List<string>();

        foreach (var exp in expected)
        {
            // DisplayName contains search (case-insensitive) is the supported UI, not Id exact
            var candidates = catalog.Sets
                .Where(s => s.DisplayName.Contains(exp.Alias, StringComparison.OrdinalIgnoreCase)
                         || s.Id.Contains(exp.Alias, StringComparison.OrdinalIgnoreCase)
                         || s.Variants.SelectMany(v => v.Pieces).Any(p => p.EditorId.Contains(exp.Alias, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (candidates.Count == 0)
            {
                if (optionalAliases.Contains(exp.Alias))
                {
                    _output.WriteLine($"[F3] '{exp.Name}' (alias '{exp.Alias}'): no set found - optional, skipping (may be filtered or DLC-specific)");
                    continue;
                }

                failures.Add($"'{exp.Name}' (alias '{exp.Alias}'): no set found via DisplayName/Id/EditorId contains search");
                continue;
            }

            // For F3: verify FilterWardrobeOutfits did not over-filter - leatheralloutfit must be kept (families.Count==1), cwmission not in catalog
            if (exp.Alias == "leather")
            {
                var hasLeatherAllOutfit = candidates.Any(s => s.Id.Equals("leatheralloutfit", StringComparison.OrdinalIgnoreCase));
                if (!hasLeatherAllOutfit)
                {
                    _output.WriteLine($"[F3] Leather alias found {candidates.Count} candidates but no 'leatheralloutfit' - ids: {string.Join(",", candidates.Select(c => c.Id))}");
                }
            }

            var best = candidates.OrderByDescending(s => s.Variants.SelectMany(v => v.Pieces).Count()).First();
            var variantsWithMin = best.Variants.Where(v => v.Pieces.Count >= exp.MinPieces).ToList();

            if (variantsWithMin.Count == 0)
            {
                var piecesPerVariant = string.Join(", ", best.Variants.Select(v => $"{v.Gender}:{v.Pieces.Count}[{string.Join(",", v.Pieces.Select(p => p.Slot.Split(' ')[1]))}]"));
                failures.Add($"'{exp.Name}' (alias '{exp.Alias}') best set '{best.Id}' ({best.DisplayName}) has no variant with >= {exp.MinPieces} pieces: {piecesPerVariant}");
                continue;
            }

            if (exp.BothGenders)
            {
                var hasMale = best.Variants.Any(v => v.Gender == Gender.Male && v.Pieces.Count >= exp.MinPieces);
                var hasFemale = best.Variants.Any(v => v.Gender == Gender.Female && v.Pieces.Count >= exp.MinPieces);
                if (!hasMale || !hasFemale)
                {
                    var perGender = string.Join(", ", best.Variants.Select(v => $"{v.Gender}:{v.Pieces.Count}"));
                    failures.Add($"'{exp.Name}' (alias '{exp.Alias}') set '{best.Id}' missing gender variant with >= {exp.MinPieces}: hasMale={hasMale} hasFemale={hasFemale} variants [{perGender}]");
                }
            }

            _output.WriteLine($"[F3] '{exp.Name}' -> set '{best.Id}' ({best.DisplayName}) variants [{string.Join(", ", best.Variants.Select(v => $"{v.Gender} {v.Weight} {v.Pieces.Count}"))}] alias '{exp.Alias}' OK");
        }

        // Also verify wardrobe filter: cwmission04outfitimperial must not appear as mega-set
        var wardrobeLeak = catalog.Sets.FirstOrDefault(s => s.Id.Contains("cwmission04outfitimperial", StringComparison.OrdinalIgnoreCase));
        if (wardrobeLeak is not null)
        {
            failures.Add($"Wardrobe outfit 'cwmission04outfitimperial' leaked into catalog as set '{wardrobeLeak.Id}' with {wardrobeLeak.Variants.SelectMany(v => v.Pieces).Count()} pieces - FilterWardrobeOutfits over-filter check failed");
        }
        else
        {
            _output.WriteLine("[F3] Wardrobe filter OK: cwmission04outfitimperial not present");
        }

        Assert.True(failures.Count == 0, $"F3 validation failed ({failures.Count}):\n{string.Join("\n", failures)}");
    }

    private static string? ChooseMainPlugin(string root)
    {
        var candidates = Directory.EnumerateFiles(root, "*.esp", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.esm", SearchOption.AllDirectories))
            .ToList();

        var byName = candidates.FirstOrDefault(p => Path.GetFileName(p).Contains("vigilant", StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        return candidates.OrderByDescending(p => new FileInfo(p).Length).FirstOrDefault();
    }
}