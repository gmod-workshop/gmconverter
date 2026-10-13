namespace GMConverter.SourceEngine;

// Bone and socket names follow many conventions; splitting them into lowercase words lets the
// guesses that read them (hit groups, hand attachments) match "upperarm_l", "LeftUpLeg" and
// "Bip01 R Calf" alike.
internal static class NameWords
{
    // "LeftUpLeg" -> left, up, leg; "upperarm_l" -> upperarm, l; "Bip01 R Calf" -> bip01, r, calf.
    public static List<string> Split(string name)
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

    public static bool IsLeft(IReadOnlyCollection<string> words) => words.Contains("l") || words.Contains("left");

    public static bool IsRight(IReadOnlyCollection<string> words) => words.Contains("r") || words.Contains("right");
}
