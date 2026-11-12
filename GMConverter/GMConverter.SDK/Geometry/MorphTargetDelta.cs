using System.Numerics;

namespace GMConverter.SDK.Geometry;

/// <summary>How far a morph target moves one vertex at full weight, and how its normal turns.</summary>
/// <param name="VertexIndex">The vertex, in the mesh's vertex list.</param>
/// <param name="Position">The change in position, in model space.</param>
/// <param name="Normal">The change in normal; added to the vertex normal and renormalized.</param>
public readonly record struct MorphTargetDelta(int VertexIndex, Vector3 Position, Vector3 Normal);
