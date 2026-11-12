using GMConverter.Unity.Assets;
using GMConverter.Unity.Scenes;

namespace GMConverter.Unity.Models;

/// <summary>
/// A mesh renderer or skinned mesh renderer found in a hierarchy, with its mesh and, for a skinned
/// one, the hierarchy node of each entry of its bone list (null for a bone outside the hierarchy).
/// </summary>
internal sealed record UnityRenderer(
    UnityTransformNode Node,
    UnityObject Renderer,
    UnityObject Mesh,
    IReadOnlyList<UnityTransformNode?> Bones)
{
    public bool IsSkinned => Bones.Count > 0;
}
