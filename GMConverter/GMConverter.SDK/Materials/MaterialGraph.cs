namespace GMConverter.SDK.Materials;

/// <summary>
/// A material as the expression its source engine evaluates per pixel, for exporters that can compile
/// shaders. Nodes are in evaluation order: each reads only earlier nodes. <see cref="Emissive"/> is the
/// light the surface gives off (linear RGB, may exceed 1); <see cref="Opacity"/> scales it for additive
/// materials and is the coverage of blended ones. Exporters that can't compile shaders use the
/// <see cref="Material"/>'s textures instead.
/// <para>A lit graph also has a <see cref="Surface"/>: what light does at the surface, shaded by its
/// <see cref="MaterialGraphSurface.Shading"/> model, with <see cref="Emissive"/> added on top and
/// <see cref="Opacity"/> as coverage (0 or 1 for cut-outs).</para>
/// </summary>
/// <param name="Nodes">The operations, in evaluation order.</param>
/// <param name="Textures">The textures <see cref="MaterialGraphOp.TextureSample"/> nodes read.</param>
/// <param name="Emissive">The node giving the emitted colour, or null for none.</param>
/// <param name="Opacity">The node giving opacity, or null for fully opaque.</param>
public sealed record MaterialGraph(
    IReadOnlyList<MaterialGraphNode> Nodes,
    IReadOnlyList<MaterialGraphTexture> Textures,
    int? Emissive,
    int? Opacity)
{
    private int[]? _sizes;

    /// <summary>The lit surface the graph describes, or null when it only emits light.</summary>
    public MaterialGraphSurface? Surface { get; init; }

    /// <summary>How many components node <paramref name="index"/> produces, 1 to 4.</summary>
    public int SizeOf(int index)
    {
        _sizes ??= ComputeSizes();
        return _sizes[index];
    }

    /// <summary>Throws when a node reads a later node, a missing texture, or has the wrong number of inputs.</summary>
    public void Validate()
    {
        for (var index = 0; index < Nodes.Count; index++)
        {
            var node = Nodes[index];
            if (node.Inputs.Any(input => input < 0 || input >= index))
            {
                throw new InvalidOperationException($"Material graph node {index} ({node.Op}) reads a node that isn't before it.");
            }

            if (node.Inputs.Count != InputCount(node.Op))
            {
                throw new InvalidOperationException($"Material graph node {index} ({node.Op}) has {node.Inputs.Count} inputs, not {InputCount(node.Op)}.");
            }

            if (node.Op == MaterialGraphOp.TextureSample && (node.Texture < 0 || node.Texture >= Textures.Count))
            {
                throw new InvalidOperationException($"Material graph node {index} samples texture {node.Texture}, which doesn't exist.");
            }

            if (node.Op == MaterialGraphOp.Swizzle && (node.Components is not { Length: >= 1 and <= 4 } || node.Components.Any(component => "xyzw".IndexOf(component, StringComparison.Ordinal) < 0)))
            {
                throw new InvalidOperationException($"Material graph node {index} has invalid swizzle '{node.Components}'.");
            }
        }

        int?[] surface = Surface is { } lit ? [lit.BaseColor, lit.Metallic, lit.Specular, lit.Roughness, lit.Normal, lit.Reflection] : [];
        foreach (var output in surface.Concat([Emissive, Opacity]))
        {
            if (output is { } value && (value < 0 || value >= Nodes.Count))
            {
                throw new InvalidOperationException($"Material graph output {value} is not a node.");
            }
        }
    }

    /// <summary>How many inputs a node of <paramref name="op"/> takes.</summary>
    public static int InputCount(MaterialGraphOp op) => op switch
    {
        MaterialGraphOp.Constant or MaterialGraphOp.TexCoord or MaterialGraphOp.Time or MaterialGraphOp.ParticleColor
            or MaterialGraphOp.ParticleParameters or MaterialGraphOp.ParticleAge or MaterialGraphOp.CameraVector
            or MaterialGraphOp.ReflectionVector or MaterialGraphOp.SceneReflection => 0,
        MaterialGraphOp.TextureSample or MaterialGraphOp.TangentToWorld or MaterialGraphOp.Swizzle or MaterialGraphOp.OneMinus or MaterialGraphOp.Floor
            or MaterialGraphOp.Ceil or MaterialGraphOp.Frac or MaterialGraphOp.Abs or MaterialGraphOp.Sqrt
            or MaterialGraphOp.Sine or MaterialGraphOp.Cosine => 1,
        MaterialGraphOp.Clamp or MaterialGraphOp.Lerp => 3,
        MaterialGraphOp.Select => 4,
        _ => 2,
    };

    private int[] ComputeSizes()
    {
        var sizes = new int[Nodes.Count];
        for (var index = 0; index < Nodes.Count; index++)
        {
            var node = Nodes[index];
            int Input(int position) => sizes[node.Inputs[position]];
            sizes[index] = node.Op switch
            {
                MaterialGraphOp.Constant => Math.Clamp(node.Size, 1, 4),
                MaterialGraphOp.TexCoord => 2,
                MaterialGraphOp.CameraVector or MaterialGraphOp.ReflectionVector or MaterialGraphOp.TangentToWorld or MaterialGraphOp.SceneReflection => 3,
                MaterialGraphOp.Time or MaterialGraphOp.ParticleAge or MaterialGraphOp.Dot => 1,
                MaterialGraphOp.ParticleColor or MaterialGraphOp.ParticleParameters or MaterialGraphOp.TextureSample => 4,
                MaterialGraphOp.Swizzle => node.Components?.Length ?? 1,
                MaterialGraphOp.Append => Math.Min(4, Input(0) + Input(1)),
                MaterialGraphOp.Clamp or MaterialGraphOp.Lerp => Input(0) == 1 ? Input(1) : Input(1) == 1 ? Input(0) : Math.Min(Input(0), Input(1)),
                MaterialGraphOp.Select => Broadcast(Input(2), Input(3)),
                _ when InputCount(node.Op) == 1 => Input(0),
                _ => Broadcast(Input(0), Input(1)),
            };
        }

        return sizes;
    }

    /// <summary>The size of an operation on values of <paramref name="a"/> and <paramref name="b"/> components.</summary>
    public static int Broadcast(int a, int b) => a == 1 ? b : b == 1 ? a : Math.Min(a, b);
}
