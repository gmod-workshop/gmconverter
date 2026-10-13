using GMConverter.SDK.Common;

namespace GMConverter.SourceEngine.Common;

// Bone and socket names follow many conventions; splitting them into lowercase words lets the
// guesses that read them (hit groups, hand attachments) match "upperarm_l", "LeftUpLeg" and
// "Bip01 R Calf" alike.
internal static class NameWords
{
    public static List<string> Split(string name) => NameHelpers.SplitWords(name);

    public static bool IsLeft(IReadOnlyCollection<string> words) => words.Contains("l") || words.Contains("left");

    public static bool IsRight(IReadOnlyCollection<string> words) => words.Contains("r") || words.Contains("right");
}
