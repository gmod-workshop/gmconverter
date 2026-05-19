namespace GMConverter.Geometry;

internal sealed class Material(
    string name,
    string? path = null,
    Texture? diffuseTexture = null,
    Texture? specularTexture = null,
    Texture? normalTexture = null,
    Texture? emissiveTexture = null,
    MaterialSurfaceKind surfaceKind = MaterialSurfaceKind.Unspecified,
    MaterialSpecularTexturePacking specularTexturePacking = MaterialSpecularTexturePacking.Standard,
    MaterialNormalTextureConvention normalTextureConvention = MaterialNormalTextureConvention.OpenGl,
    System.Numerics.Vector2? bakedUv0Scale = null,
    float specularFactor = 1.0f)
{
    public string Name { get; } = name;

    public string? Path { get; } = path;

    public Texture? DiffuseTexture { get; } = diffuseTexture;

    public Texture? SpecularTexture { get; } = specularTexture;

    public Texture? NormalTexture { get; } = normalTexture;

    public Texture? EmissiveTexture { get; } = emissiveTexture;

    public MaterialSurfaceKind SurfaceKind { get; } = surfaceKind;

    public MaterialSpecularTexturePacking SpecularTexturePacking { get; } = specularTexturePacking;

    public MaterialNormalTextureConvention NormalTextureConvention { get; } = normalTextureConvention;

    // When the multi-layer baker writes a tile-extended texture (e.g. 3*W wide because the mesh's
    // UV0 spans [0,3]), the renderer needs to scale the mesh's UVs to fit the [0,1] range of the
    // baked texture in glTF (which is always sampled in [0,1] regardless of pixel size). This
    // value matches FortnitePorting's per-pixel tile selection by remapping the mesh's UVs.
    public System.Numerics.Vector2? BakedUv0Scale { get; } = bakedUv0Scale;

    // Multiplier applied to the dielectric specular response at export time (glTF
    // KHR_materials_specular.specularFactor; Source phong intensity, etc.). Defaults to 1.0 so
    // the standard PBR dielectric F0 = 0.04 carries through unchanged. Format-specific importers
    // set this when the source asset's specular convention doesn't line up with the glTF default
    // — e.g. Fortnite materials need ≈0.01 to match the very subtle in-game highlights.
    public float SpecularFactor { get; } = specularFactor;

    public bool HasAlpha => DiffuseTexture?.HasAlpha ?? false;

    public bool IsIlluminated => EmissiveTexture is not null;

    public IEnumerable<Texture> Textures
    {
        get
        {
            if (DiffuseTexture is not null)
            {
                yield return DiffuseTexture;
            }

            if (SpecularTexture is not null)
            {
                yield return SpecularTexture;
            }

            if (NormalTexture is not null)
            {
                yield return NormalTexture;
            }

            if (EmissiveTexture is not null)
            {
                yield return EmissiveTexture;
            }
        }
    }
}
