using GMConverter.SDK.Geometry;
using GMConverter.Unity.Scenes;

namespace GMConverter.Unity.Models;

/// <summary>The skeleton built from a hierarchy, and the bone each kept hierarchy node became.</summary>
internal sealed record UnitySkeleton(Skeleton Skeleton, IReadOnlyDictionary<UnityTransformNode, int> Indices)
{
    /// <summary>The bone of <paramref name="node"/>, or of its nearest kept ancestor.</summary>
    public int IndexOf(UnityTransformNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (Indices.TryGetValue(current, out var index))
            {
                return index;
            }
        }

        return 0;
    }
}
