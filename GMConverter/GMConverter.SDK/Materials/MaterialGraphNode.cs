using System.Numerics;

namespace GMConverter.SDK.Materials;

/// <summary>One operation in a <see cref="MaterialGraph"/>; its inputs are indices of earlier nodes.</summary>
/// <param name="Op">What the node computes.</param>
/// <param name="Inputs">The nodes it reads, by index; each lower than this node's own.</param>
public sealed record MaterialGraphNode(MaterialGraphOp Op, IReadOnlyList<int> Inputs)
{
    /// <summary>A constant's value.</summary>
    public Vector4 Value { get; init; }

    /// <summary>A constant's component count, 1 to 4.</summary>
    public int Size { get; init; } = 1;

    /// <summary>A swizzle's components, from "xyzw".</summary>
    public string? Components { get; init; }

    /// <summary>A texture sample's index into <see cref="MaterialGraph.Textures"/>.</summary>
    public int Texture { get; init; }

    /// <summary>A constant's value as a single number, or null when the node isn't a constant with every component equal.</summary>
    public float? Uniform => Op == MaterialGraphOp.Constant && (Size < 2 || Value.Y == Value.X) && (Size < 3 || Value.Z == Value.X) && (Size < 4 || Value.W == Value.X)
        ? Value.X
        : null;

    public static MaterialGraphNode Constant(float value) => new(MaterialGraphOp.Constant, []) { Value = new Vector4(value, 0f, 0f, 0f), Size = 1 };

    public static MaterialGraphNode Constant(Vector4 value, int size) => new(MaterialGraphOp.Constant, []) { Value = value, Size = size };
}
