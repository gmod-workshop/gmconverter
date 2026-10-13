using GMConverter.SDK.Geometry;

namespace GMConverter.UnrealEngine.Importers;

// UE4/5 characters built on the Epic mannequin skeleton name their limbs foot_l, foot_r, hand_l
// and hand_r, each two bones below its thigh or upper arm, and add ik_foot_* / ik_hand_* target
// bones for the game's runtime IK. The limb names are enough to find the chains; the ik_* bones
// only follow them.
internal static class UnrealIkChains
{
    private static readonly (string BoneName, IkChainKind Kind)[] _limbs =
    [
        ("foot_l", IkChainKind.LeftFoot),
        ("foot_r", IkChainKind.RightFoot),
        ("hand_l", IkChainKind.LeftHand),
        ("hand_r", IkChainKind.RightHand),
    ];

    public static IReadOnlyList<IkChain>? Find(Skeleton? skeleton)
    {
        if (skeleton is null)
        {
            return null;
        }

        var bones = skeleton.Bones.ToDictionary(bone => bone.Index);
        List<IkChain> chains = [];
        foreach (var (boneName, kind) in _limbs)
        {
            var end = skeleton.Bones.FirstOrDefault(bone => string.Equals(bone.Name, boneName, StringComparison.OrdinalIgnoreCase));
            if (end is not null &&
                bones.TryGetValue(end.ParentIndex, out var middle) &&
                bones.ContainsKey(middle.ParentIndex))
            {
                chains.Add(new IkChain(kind, end.Index));
            }
        }

        return chains.Count == 0 ? null : chains;
    }
}
