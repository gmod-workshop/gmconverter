using System.Numerics;
using GMConverter.Godot.Resources;

namespace GMConverter.Godot.Geometry;

/// <summary>
/// One surface of a Godot mesh, decoded into Godot's own space: triangles drawing with one
/// material. <see cref="Bones"/> and <see cref="Weights"/> hold <see cref="InfluencesPerVertex"/>
/// entries per vertex, the bones being indices into the mesh instance's skin.
/// </summary>
internal sealed record GodotSurface(
    string Name,
    Vector3[] Positions,
    Vector3[]? Normals,
    Vector2[]? TextureCoordinates,
    Vector4[]? Colors,
    int[]? Bones,
    float[]? Weights,
    int InfluencesPerVertex,
    int[] Indices,
    GodotResource? Material)
{
    public int VertexCount => Positions.Length;

    public bool IsSkinned => Bones is not null && Weights is not null;
}
