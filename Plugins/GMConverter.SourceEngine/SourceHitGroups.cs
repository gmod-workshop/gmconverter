namespace GMConverter.SourceEngine;

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
        var words = SplitWords(boneName);
        bool Has(string[] candidates) => words.Any(word => candidates.Any(candidate => word.StartsWith(candidate, StringComparison.Ordinal)));
        var isLeft = words.Contains("l") || words.Contains("left");
        var isRight = words.Contains("r") || words.Contains("right");

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

    // "LeftUpLeg" -> left, up, leg; "upperarm_l" -> upperarm, l; "Bip01 R Calf" -> bip01, r, calf.
    private static List<string> SplitWords(string name)
    {
        List<string> words = [];
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
        }

        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            if (char.IsUpper(character) && i > 0 && char.IsLower(name[i - 1]))
            {
                Flush();
            }

            current.Append(character);
        }

        Flush();
        return words;
    }
}
