using GMConverter.SourceEngine.Physics;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceHitGroupsTests
{
    [Theory]
    [InlineData("head", 1)]
    [InlineData("ValveBiped.Bip01_Head1", 1)]
    [InlineData("spine_02", 2)]
    [InlineData("pelvis", 3)]
    [InlineData("upperarm_l", 4)]
    [InlineData("RightHand", 5)]
    [InlineData("LeftUpLeg", 6)]
    [InlineData("Bip01 R Calf", 7)]
    [InlineData("backpack", 0)]
    public void BoneNamesMapToHitGroups(string boneName, int hitGroup)
    {
        Assert.Equal(hitGroup, SourceHitGroups.FromBoneName(boneName));
    }
}
