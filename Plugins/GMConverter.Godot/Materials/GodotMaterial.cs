using System.Numerics;
using GMConverter.SDK.Materials;

namespace GMConverter.Godot.Materials;

/// <summary>
/// A converted Godot material and the scale and offset of its texture coordinates
/// (<c>uv1_scale</c>, <c>uv1_offset</c>), which the SDK has no field for, so the model builder
/// applies them to the texture coordinates of the surfaces using it.
/// </summary>
internal sealed record GodotMaterial(Material Material, Vector2 UvScale, Vector2 UvOffset)
{
    public bool HasUvTransform => UvScale != Vector2.One || UvOffset != Vector2.Zero;

    /// <summary>Applies the transform in Godot's texture space, where V = 0 is the top of the image.</summary>
    public Vector2 Apply(Vector2 uv)
    {
        return (uv * UvScale) + UvOffset;
    }
}
