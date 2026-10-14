using System.Numerics;

namespace GMConverter.Unity.Geometry;

/// <summary>
/// A Unity mesh decoded into Unity space. Bone influences hold four slots per vertex (unused slots
/// have weight 0) indexing the renderer's bone list; <see cref="BindPoses"/> maps mesh space into
/// each bone's space, as System.Numerics row-vector matrices.
/// </summary>
internal sealed record UnityMeshData(
    string Name,
    Vector3[] Positions,
    Vector3[]? Normals,
    Vector2[]? TextureCoordinates,
    int[]? BoneIndices,
    float[]? BoneWeights,
    IReadOnlyList<int[]> Submeshes,
    Matrix4x4[] BindPoses)
{
    public const int InfluencesPerVertex = 4;

    public int VertexCount => Positions.Length;

    public bool IsSkinned => BoneIndices is not null && BindPoses.Length > 0;
}
