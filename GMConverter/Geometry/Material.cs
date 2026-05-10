namespace GMConverter.Geometry;

internal sealed class Material(
    string name,
    string? path = null,
    Texture? diffuseTexture = null,
    Texture? specularTexture = null,
    Texture? normalTexture = null,
    Texture? emissiveTexture = null)
{
    public string Name { get; } = name;

    public string? Path { get; } = path;

    public Texture? DiffuseTexture { get; } = diffuseTexture;

    public Texture? SpecularTexture { get; } = specularTexture;

    public Texture? NormalTexture { get; } = normalTexture;

    public Texture? EmissiveTexture { get; } = emissiveTexture;

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
