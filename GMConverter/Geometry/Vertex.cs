using System.Numerics;

namespace GMConverter.Geometry;

internal sealed record Vertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 TextureCoordinate,
    IReadOnlyList<VertexBoneWeight>? BoneWeights = null);