namespace GMConverter.SDK.Materials;

/// <summary>How a lit <see cref="MaterialGraphSurface"/> turns light into colour.</summary>
public enum MaterialShading
{
    /// <summary>
    /// Unreal's GGX metal/roughness model, as CrossFire HD's UE3 and Unreal Engine 4 compile it:
    /// Lambert diffuse from <c>BaseColor * (1 - Metallic)</c>; specular colour
    /// <c>F0 = lerp(0.08 * Specular, BaseColor, Metallic)</c>; with <c>a = Roughness²</c> (roughness at
    /// least 0.004), the GGX distribution of <c>a</c>, Smith's joint visibility approximation and
    /// Schlick's Fresnel towards <c>saturate(50 * F0.g)</c>. Reflections are weighed by the same
    /// model integrated over the hemisphere (split-sum).
    /// </summary>
    UnrealGgx,
}
