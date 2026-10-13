using GMConverter.SDK.Geometry;
using GMConverter.UnrealEngine.Importers;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UnrealIkChainsTests
{
    [Fact]
    public void MannequinLimbsBecomeChainsByTheirEndBones()
    {
        string[] names = ["root", "pelvis", "thigh_l", "calf_l", "foot_l", "upperarm_r", "lowerarm_r", "Hand_R", "ik_foot_root", "ik_foot_l"];
        int[] parents = [-1, 0, 1, 2, 3, 1, 5, 6, 0, 8];
        var skeleton = new Skeleton([.. names.Select((name, index) => new Bone(index, name, parents[index], Transform.Identity))]);

        var chains = UnrealIkChains.Find(skeleton);

        // The IK target bones are not limbs; a right foot and a left hand are missing.
        Assert.Equal([new IkChain(IkChainKind.LeftFoot, 4), new IkChain(IkChainKind.RightHand, 7)], chains);
    }

    [Fact]
    public void AnEndBoneWithoutTwoParentsIsNotAChain()
    {
        var skeleton = new Skeleton([new Bone(0, "calf_l", -1, Transform.Identity), new Bone(1, "foot_l", 0, Transform.Identity)]);

        Assert.Null(UnrealIkChains.Find(skeleton));
    }
}
