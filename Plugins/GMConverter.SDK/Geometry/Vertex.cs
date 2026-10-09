using System.Numerics;

namespace GMConverter.SDK.Geometry;

public sealed record Vertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 TextureCoordinate,
    IReadOnlyList<VertexBoneWeight>? BoneWeights = null);
