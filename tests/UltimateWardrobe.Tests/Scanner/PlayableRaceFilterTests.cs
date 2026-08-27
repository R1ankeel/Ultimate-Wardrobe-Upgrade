using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class PlayableRaceFilterTests
{
    [Theory]
    [InlineData("ArgonianRace")]
    [InlineData("BretonRace")]
    [InlineData("DarkElfRace")]
    [InlineData("HighElfRace")]
    [InlineData("ImperialRace")]
    [InlineData("KhajiitRace")]
    [InlineData("NordRace")]
    [InlineData("OrcRace")]
    [InlineData("RedguardRace")]
    [InlineData("WoodElfRace")]
    public void IsInPlayableWhitelist_AllTenBaseRaces(string raceEditorId)
    {
        Assert.True(PlayableRaceFilter.IsInPlayableWhitelist(raceEditorId));
        Assert.True(PlayableRaceFilter.IsBaseRaceId(raceEditorId));
    }

    [Theory]
    [InlineData("ArgonianRaceVampire")]
    [InlineData("BretonRaceVampire")]
    [InlineData("DarkElfRaceVampire")]
    [InlineData("HighElfRaceVampire")]
    [InlineData("ImperialRaceVampire")]
    [InlineData("KhajiitRaceVampire")]
    [InlineData("NordRaceVampire")]
    [InlineData("OrcRaceVampire")]
    [InlineData("RedguardRaceVampire")]
    [InlineData("WoodElfRaceVampire")]
    public void IsInPlayableWhitelist_AllTenVampireVariants_NeverSkip(string raceEditorId)
    {
        Assert.True(PlayableRaceFilter.IsInPlayableWhitelist(raceEditorId));
        Assert.False(PlayableRaceFilter.IsBaseRaceId(raceEditorId));
    }

    [Fact]
    public void IsInPlayableWhitelist_DefaultRace_UniversalHumanArmor_NeverSkips()
    {
        Assert.True(PlayableRaceFilter.IsInPlayableWhitelist("DefaultRace"));
        Assert.False(PlayableRaceFilter.IsBaseRaceId("DefaultRace"));
    }

    [Theory]
    [InlineData("BoarRace")]
    [InlineData("ChaurusRace")]
    [InlineData("ChickenRace")]
    [InlineData("CaveBearRace")]
    [InlineData("MudcrabRace")]
    [InlineData("NordRaceChild")]
    [InlineData("ElderRace")]
    [InlineData("DraugrRace")]
    [InlineData("WerewolfBeastRace")]
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
    public void WhitelistIsCaseSensitive_LowercaseNordRaceNeverMatches()
    {
        Assert.False(PlayableRaceFilter.IsInPlayableWhitelist("nordrace"));
    }
}