using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.Unity.Scenes;

namespace GMConverter.Unity.Models;

/// <summary>
/// A shape a game makes in code by turning one node, such as a jaw opened while a character talks,
/// baked into a morph target: everything skinned to the node or below it moves as the turn moves it.
/// </summary>
/// <param name="Name">The morph target's name.</param>
/// <param name="Role">What the shape is for.</param>
/// <param name="Pivot">The node the game turns.</param>
/// <param name="Turn">The turn at full weight, applied in the pivot's own frame (Unity space).</param>
internal sealed record UnityPoseMorph(string Name, MorphTargetRole Role, UnityTransformNode Pivot, Quaternion Turn)
{
    /// <summary>Whether the turn moves <paramref name="node"/>: the pivot and everything below it.</summary>
    public bool Moves(UnityTransformNode node)
    {
        return node == Pivot || node.Ancestors().Contains(Pivot);
    }

    /// <summary>
    /// What the turn does to the world matrix of a node it moves (<c>World * Change()</c>). The
    /// pivot's world matrix becomes <c>Turn * World</c>, and nodes below it follow.
    /// </summary>
    public Matrix4x4 Change()
    {
        return Change(Pivot.World, Turn);
    }

    /// <summary>The change for a pivot with world matrix <paramref name="pivotWorld"/> turning by <paramref name="turn"/> in its own frame.</summary>
    public static Matrix4x4 Change(Matrix4x4 pivotWorld, Quaternion turn)
    {
        Matrix4x4.Invert(pivotWorld, out var inverse);
        return inverse * Matrix4x4.CreateFromQuaternion(turn) * pivotWorld;
    }
}
