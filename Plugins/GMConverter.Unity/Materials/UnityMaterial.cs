using System.Numerics;
using GMConverter.SDK.Materials;

namespace GMConverter.Unity.Materials;

/// <summary>
/// A converted Unity material and the tiling and offset of its base texture, which the SDK has no
/// field for, so the model builder applies it to the texture coordinates of the surfaces using it.
/// </summary>
internal sealed record UnityMaterial(Material Material, Vector2 UvScale, Vector2 UvOffset)
{
    public bool HasUvTransform => UvScale != Vector2.One || UvOffset != Vector2.Zero;

    public Vector2 Apply(Vector2 uv)
    {
        return uv * UvScale + UvOffset;
    }
}
