using System.Numerics;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Scenes;

/// <summary>
/// One transform of a loaded hierarchy, in Unity space. <see cref="World"/> is relative to the
/// hierarchy's root, whose own placement in its scene is dropped.
/// </summary>
internal sealed class UnityTransformNode
{
    public required UnityObject Transform { get; init; }

    public required UnityObject? GameObject { get; init; }

    public required string Name { get; init; }

    public required Vector3 LocalPosition { get; init; }

    public required Quaternion LocalRotation { get; init; }

    public required Vector3 LocalScale { get; init; }

    public required Matrix4x4 World { get; init; }

    public required UnityTransformNode? Parent { get; init; }

    public required bool IsActive { get; init; }

    public List<UnityTransformNode> Children { get; } = [];

    public IEnumerable<UnityTransformNode> Ancestors()
    {
        for (var node = Parent; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }

    public IEnumerable<UnityTransformNode> DepthFirst()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(child => child.DepthFirst()))
        {
            yield return descendant;
        }
    }
}
