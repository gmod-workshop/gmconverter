namespace GMConverter.SDK.Materials;

/// <summary>
/// What a <see cref="MaterialGraphNode"/> computes. Values are vectors of one to four floats in
/// linear light; arithmetic broadcasts a one-component operand across the other, and otherwise
/// works on the components both operands have (see <see cref="MaterialGraph.SizeOf"/>).
/// </summary>
public enum MaterialGraphOp
{
    /// <summary><see cref="MaterialGraphNode.Value"/>, with <see cref="MaterialGraphNode.Size"/> components.</summary>
    Constant,

    /// <summary>The surface's first texture coordinate (2), in image space: U to the right, V down the stored image.</summary>
    TexCoord,

    /// <summary>Seconds of game time (1), for animation that runs regardless of the particle.</summary>
    Time,

    /// <summary>The particle's colour, linear RGB that may exceed 1, and alpha (4).</summary>
    ParticleColor,

    /// <summary>The particle's four <see cref="Particles.ParticleEmitter.Parameters"/> (4), 0 where unset.</summary>
    ParticleParameters,

    /// <summary>The particle's age from 0 at birth to 1 at death (1).</summary>
    ParticleAge,

    /// <summary>
    /// <see cref="MaterialGraph.Textures"/>[<see cref="MaterialGraphNode.Texture"/>] at coordinate input 0 (4),
    /// colour decoded to linear when the texture is sRGB.
    /// </summary>
    TextureSample,

    /// <summary>Input 0's components named by <see cref="MaterialGraphNode.Components"/> ("xyz", "w", "xxx"...).</summary>
    Swizzle,

    /// <summary>Input 0's components followed by input 1's (at most 4).</summary>
    Append,

    Add,
    Subtract,
    Multiply,
    Divide,

    /// <summary>Input 0 raised to input 1, with input 0 taken as 0 where negative.</summary>
    Power,
    Min,
    Max,

    /// <summary>Dot product of the components both inputs have (1).</summary>
    Dot,
    OneMinus,
    Floor,
    Ceil,
    Frac,
    Abs,

    /// <summary>Square root, with negative inputs taken as 0.</summary>
    Sqrt,

    /// <summary>Sine of input 0 in radians.</summary>
    Sine,

    /// <summary>Cosine of input 0 in radians.</summary>
    Cosine,

    /// <summary>Input 0 clamped between inputs 1 and 2.</summary>
    Clamp,

    /// <summary>Input 0 to input 1 by input 2.</summary>
    Lerp,

    /// <summary>Input 2 where input 0 is greater than input 1, else input 3 (inputs 0 and 1 have one component).</summary>
    Select,
}
