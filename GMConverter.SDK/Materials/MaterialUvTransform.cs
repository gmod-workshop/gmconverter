using System.Numerics;

namespace GMConverter.SDK.Materials;

/// <summary>
/// Static and animated transform of a texture layer's coordinates, in image space (U to the
/// right, V down the stored image) like <see cref="Material.UvScrollRate"/>. Scale and rotation
/// pivot on <see cref="Center"/>; a scale of 0.5 shows half the texture across the surface.
/// </summary>
public sealed record MaterialUvTransform
{
    public Vector2 Scale { get; init; } = Vector2.One;

    public Vector2 Center { get; init; }

    public float RotationDegrees { get; init; }

    public float RotationRateDegrees { get; init; }

    // Texture widths and heights per second.
    public Vector2 ScrollRate { get; init; }

    public MaterialOscillation? U { get; init; }

    public MaterialOscillation? V { get; init; }

    public bool IsAnimated => RotationRateDegrees != 0f || ScrollRate != Vector2.Zero || U is not null || V is not null;

    public bool IsIdentity => !IsAnimated && Scale == Vector2.One && RotationDegrees == 0f;
}
