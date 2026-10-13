using GMConverter.SourceEngine.Common;

namespace GMConverter.SourceEngine.Physics;

// Source hit groups decide where damage lands (head shots, limb hits). Physics data names bones
// but not body regions, so the group is guessed from common bone naming (head, spine_02,
// upperarm_l, LeftUpLeg, Bip01 R Calf); anything unrecognised stays generic.
internal static class SourceHitGroups
{
    private const int _generic = 0;
    private const int _head = 1;
    private const int _chest = 2;
    private const int _stomach = 3;
    private const int _leftArm = 4;
    private const int _rightArm = 5;
    private const int _leftLeg = 6;
    private const int _rightLeg = 7;

    private static readonly string[] _headWords = ["head", "neck", "jaw", "eye"];
    private static readonly string[] _chestWords = ["chest", "spine", "torso", "clavicle", "shoulder", "ribs"];
    private static readonly string[] _stomachWords = ["pelvis", "hip", "hips", "stomach", "abdomen", "belly", "waist"];
    private static readonly string[] _armWords = ["arm", "upperarm", "lowerarm", "forearm", "elbow", "hand", "wrist", "finger", "thumb", "mitten", "index", "middle", "ring", "pinky"];
    private static readonly string[] _legWords = ["leg", "upleg", "thigh", "calf", "knee", "shin", "foot", "ankle", "toe", "ball"];

    public static int FromBoneName(string boneName)
    {
        var words = NameWords.Split(boneName);
        bool Has(string[] candidates) => words.Any(word => candidates.Any(candidate => word.StartsWith(candidate, StringComparison.Ordinal)));
        var isLeft = NameWords.IsLeft(words);
        var isRight = NameWords.IsRight(words);

        if (Has(_headWords))
        {
            return _head;
        }

        if (Has(_armWords) && isLeft != isRight)
        {
            return isLeft ? _leftArm : _rightArm;
        }

        if (Has(_legWords) && isLeft != isRight)
        {
            return isLeft ? _leftLeg : _rightLeg;
        }

        if (Has(_chestWords))
        {
            return _chest;
        }

        return Has(_stomachWords) ? _stomach : _generic;
    }
}
