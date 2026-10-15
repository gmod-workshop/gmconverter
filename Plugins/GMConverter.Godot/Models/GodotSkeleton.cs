using GMConverter.SDK.Geometry;

namespace GMConverter.Godot.Models;

/// <summary>A converted skeleton and the bone each scene node or <c>Skeleton3D</c> bone became.</summary>
internal sealed record GodotSkeleton(Skeleton Skeleton, IReadOnlyDictionary<GodotBoneKey, int> Indices)
{
    public int IndexOf(GodotBoneKey key)
    {
        return Indices.TryGetValue(key, out var index) ? index : 0;
    }
}
