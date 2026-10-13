using GMConverter.SDK.Audio;

namespace GMConverter.UnrealEngine.Audio;

/// <summary>
/// Flattens a Wwise event's sound structure into one choice of media, the most a Source sound
/// definition (or any other single sound event) can express.
/// </summary>
/// <remarks>
/// What cannot be kept is reduced: an event or layer container that plays several things at once
/// keeps only its first, switch containers have already been narrowed to their default branch,
/// and sequences become random. Each media file's volume relative to the loudest becomes its
/// gain, and the loudest one's mix becomes the event's.
/// </remarks>
internal static class WwiseEventFlattener
{
    /// <param name="root">The node the event plays.</param>
    /// <param name="inherited">The mix of the actor mixers above <paramref name="root"/>.</param>
    public static WwiseEventMix? Flatten(WwiseNode root, WwiseMix inherited)
    {
        List<(uint MediaId, double Probability, WwiseMix Mix)> leaves = [];
        Collect(root, inherited, 1.0, leaves);
        if (leaves.Count == 0)
        {
            return null;
        }

        // The same media can be reached twice (shared by two branches); it plays as often as both.
        var merged = leaves
            .GroupBy(leaf => leaf.MediaId)
            .Select(group => (MediaId: group.Key, Probability: group.Sum(leaf => leaf.Probability), group.First().Mix))
            .ToList();
        var loudest = merged.MaxBy(leaf => leaf.Mix.VolumeDb).Mix;
        var entries = merged
            .Select(leaf => new WwiseEventEntry(leaf.MediaId, (float)leaf.Probability, leaf.Mix.VolumeDb - loudest.VolumeDb))
            .ToList();
        return new WwiseEventMix(entries, loudest, SelectionOf(root));
    }

    private static void Collect(WwiseNode node, WwiseMix parent, double probability, List<(uint, double, WwiseMix)> leaves)
    {
        var mix = parent.Then(node.Mix);
        switch (node.Kind)
        {
            case WwiseNodeKind.Sound:
                if (node.MediaId != 0)
                {
                    leaves.Add((node.MediaId, probability, mix));
                }

                break;
            case WwiseNodeKind.Layer:
                if (node.Children.Count > 0)
                {
                    Collect(node.Children[0].Node, mix, probability, leaves);
                }

                break;
            default:
                var total = node.Children.Sum(child => Math.Max(child.Weight, 0f));
                foreach (var child in node.Children)
                {
                    var share = total > 0f ? Math.Max(child.Weight, 0f) / total : 1.0 / node.Children.Count;
                    if (share > 0)
                    {
                        Collect(child.Node, mix, probability * share, leaves);
                    }
                }

                break;
        }
    }

    // The container nearest the event that chooses decides how the event chooses.
    private static SoundSelection SelectionOf(WwiseNode node)
    {
        var current = node;
        while (true)
        {
            switch (current.Kind)
            {
                case WwiseNodeKind.Random:
                    return current.AvoidRepeats ? SoundSelection.RandomNoRepeat : SoundSelection.Random;
                case WwiseNodeKind.Sequence:
                    return SoundSelection.Sequence;
                case WwiseNodeKind.Sound:
                    return SoundSelection.Random;
            }

            if (current.Children.Count == 0)
            {
                return SoundSelection.Random;
            }

            current = current.Children[0].Node;
        }
    }
}
