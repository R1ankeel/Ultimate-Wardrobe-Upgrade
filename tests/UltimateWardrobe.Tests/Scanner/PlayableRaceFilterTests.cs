using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class PlayableRaceFilterTests
{
    [Theory]
    [InlineData("Argonian")]
    [InlineData("Breton")]
    [InlineData("DarkElf")]
    [InlineData("HighElf")]
    [InlineData("Imperial")]
    [InlineData("Khajiit")]
    [InlineData("Nord")]
    [InlineData("Orc")]
    [InlineData("Redguard")]
    [InlineData("WoodElf")]
    public void IsInPlayableWhitelist_AllTenBaseRaces(string raceEditorId)
    {
        Assert.True(PlayableRaceFilter.IsInPlayableWhitelist(raceEditorId));
        Assert.True(PlayableRaceFilter.IsBaseRaceId(raceEditorId));
    }

    [Theory]
    [InlineData("ArgonianVampire")]
    [InlineData("BretonVampire")]
    [InlineData("DarkElfVampire")]
    [InlineData("HighElfVampire")]
    [InlineData("ImperialVampire")]
    [InlineData("KhajiitVampire")]
    [InlineData("NordVampire")]
    [InlineData("OrcVampire")]
    [InlineData("RedguardVampire")]
    [InlineData("WoodElfVampire")]
    public void IsInPlayableWhitelist_AllTenVampireVariants_NeverSkip(string raceEditorId)
    {
        Assert.True(PlayableRaceFilter.IsInPlayableWhitelist(raceEditorId));
        Assert.False(PlayableRaceFilter.IsBaseRaceId(raceEditorId));
    }

    [Theory]
    [InlineData("BoarRace")]
    [InlineData("ChaurusRace")]
    [InlineData("ChickenRace")]
    [InlineData("CaveBearRace")]
    [InlineData("MudCrab")]
    [InlineData("")]
    public void IsInPlayableWhitelist_CreatureRaces_Skip(string raceEditorId)
    {
        Assert.False(PlayableRaceFilter.IsInPlayableWhitelist(raceEditorId));
    }

    [Fact]
    public void IsInPlayableWhitelist_Null_DoesNotSkip()
    {
        Assert.False(PlayableRaceFilter.IsInPlayableWhitelist(null));
    }

    [Fact]
    public void WhitelistIsCaseSensitive_LowercaseNordNeverMatches()
    {
        Assert.False(PlayableRaceFilter.IsInPlayableWhitelist("nord"));
    }
}