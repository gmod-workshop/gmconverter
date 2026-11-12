using System.Numerics;
using GMConverter.SDK.Geometry;

namespace GMConverter.Godot.Models;

/// <summary>
/// A converted skeleton, the bone each scene node or <c>Skeleton3D</c> bone became, and each
/// bone's transform relative to its parent in Godot's space, which animations start from.
/// </summary>
internal sealed record GodotSkeleton(Skeleton Skeleton, IReadOnlyDictionary<GodotBoneKey, int> Indices, IReadOnlyList<Matrix4x4> Locals)
{
    public int IndexOf(GodotBoneKey key)
    {
        return Indices.TryGetValue(key, out var index) ? index : 0;
    }
}
