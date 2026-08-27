using Mutagen.Bethesda.Skyrim;
using UltimateWardrobe.Scanner;
using Xunit;

namespace UltimateWardrobe.Tests.Scanner;

public sealed class BipedSlotMapperTests
{
    [Theory]
    [InlineData(BipedObjectFlag.Head, "30 Head")]
    [InlineData(BipedObjectFlag.Hair, "31 Hair")]
    [InlineData(BipedObjectFlag.Body, "32 Body")]
    [InlineData(BipedObjectFlag.Hands, "33 Hands")]
    [InlineData(BipedObjectFlag.Forearms, "34 Forearms")]
    [InlineData(BipedObjectFlag.Amulet, "35 Amulet")]
    [InlineData(BipedObjectFlag.Ring, "36 Ring")]
    [InlineData(BipedObjectFlag.Feet, "37 Feet")]
    [InlineData(BipedObjectFlag.Calves, "38 Calves")]
    [InlineData(BipedObjectFlag.Shield, "39 Shield")]
    [InlineData(BipedObjectFlag.Tail, "40 Tail")]
    [InlineData(BipedObjectFlag.LongHair, "41 LongHair")]
    [InlineData(BipedObjectFlag.Circlet, "42 Circlet")]
    [InlineData(BipedObjectFlag.Ears, "43 Ears")]
    public void ToSlotString_Matches_FrozenFormat(BipedObjectFlag flag, string expected)
    {
        Assert.Equal(expected, BipedSlotMapper.ToSlotString(flag));
    }

    [Fact]
    public void ToSlotString_PicksPrimaryFlag_InTableOrder()
    {
        Assert.Equal("30 Head", BipedSlotMapper.ToSlotString(BipedObjectFlag.Head | BipedObjectFlag.Circlet));

        Assert.Equal("31 Hair", BipedSlotMapper.ToSlotString(BipedObjectFlag.Hair | BipedObjectFlag.LongHair));

        Assert.Equal("32 Body", BipedSlotMapper.ToSlotString(BipedObjectFlag.Body | BipedObjectFlag.Forearms));
    }

    [Fact]
    public void ToSlotString_ReturnsNull_WhenNoRecognizedSlot()
    {
        Assert.Null(BipedSlotMapper.ToSlotString((BipedObjectFlag)0));
        Assert.Null(BipedSlotMapper.ToSlotString(BipedObjectFlag.DecapitateHead));
        Assert.Null(BipedSlotMapper.ToSlotString(BipedObjectFlag.Decapitate));
    }

    [Fact]
    public void SlotIndex_FollowsTableOrder()
    {
        Assert.True(BipedSlotMapper.SlotIndex(BipedObjectFlag.Head) < BipedSlotMapper.SlotIndex(BipedObjectFlag.Hair));
        Assert.True(BipedSlotMapper.SlotIndex(BipedObjectFlag.Feet) < BipedSlotMapper.SlotIndex(BipedObjectFlag.Shield));
        Assert.Equal(0, BipedSlotMapper.SlotIndex(BipedObjectFlag.Head));
        Assert.Equal(13, BipedSlotMapper.SlotIndex(BipedObjectFlag.Ears));
        Assert.Equal(int.MaxValue, BipedSlotMapper.SlotIndex((BipedObjectFlag)0));
    }

    [Fact]
    public void Table_UsesUniqueSlotNumbers_AndFrozenOrder()
    {
        var numbers = BipedSlotMapper.Table.Select(e => e.Slot).ToList();
        Assert.Equal(numbers.Distinct().Count(), numbers.Count);

        Assert.Equal(
            new[] { 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43 },
            numbers);
    }
}