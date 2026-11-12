using System.Numerics;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Textures;

namespace GMConverter.Unity.Materials;

/// <summary>
/// Converts Unity materials for one import. Reads the HDRP Lit and Unlit, URP and built-in
/// pipeline property names: base colour map and tint, normal map, HDRP mask map (metallic and
/// smoothness) and emission. Materials and textures are shared across the import and named
/// uniquely, since Unity allows duplicate names.
/// </summary>
internal sealed class UnityMaterialBuilder(UnityAssetLibrary library, ITextureFactory textureFactory)
{
    private const string _defaultMaterialName = "default";
    private const float _defaultSpecular = 0.5f;
    private const int _solidTextureSize = 4;

    private static readonly string[] _tintProperties = ["_BaseColor", "_UnlitColor", "_Color"];

    // Colour properties every HDRP or shader graph material carries that are not a surface colour.
    private static readonly HashSet<string> _nonSurfaceColors = new(StringComparer.Ordinal)
    {
        "_EmissionColor", "_EmissiveColor", "_EmissiveColorLDR", "_SpecColor", "_SpecularColor",
        "_DoubleSidedConstants", "_DiffusionProfileAsset", "_TransmittanceColor", "_UVMappingMask", "_UVDetailsMappingMask",
    };

    private readonly Dictionary<UnityObjectKey, UnityMaterial> _materials = [];
    private readonly List<UnityMaterial> _converted = [];
    private readonly Dictionary<UnityObjectKey, UnityTextureImage?> _images = [];
    private readonly Dictionary<(UnityObjectKey, string), Texture> _textures = [];
    private readonly HashSet<string> _materialNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _textureNames = new(StringComparer.OrdinalIgnoreCase);
    private UnityMaterial? _defaultMaterial;

    /// <summary>Every material converted so far, in the order first used.</summary>
    public IReadOnlyList<UnityMaterial> Converted => _converted;

    /// <summary>Textures whose format could not be decoded, such as Crunch-compressed ones.</summary>
    public List<string> SkippedTextures { get; } = [];

    /// <summary>The converted material, or a plain default one for a missing material.</summary>
    public UnityMaterial Build(UnityObject? material)
    {
        if (material is null)
        {
            if (_defaultMaterial is null)
            {
                _defaultMaterial = new UnityMaterial(new Material(UniqueMaterialName(_defaultMaterialName)), Vector2.One, Vector2.Zero);
                _converted.Add(_defaultMaterial);
            }

            return _defaultMaterial;
        }

        if (!_materials.TryGetValue(material.Key, out var converted))
        {
            converted = Convert(UnityMaterialProperties.Read(library, material));
            _materials[material.Key] = converted;
            _converted.Add(converted);
        }

        return converted;
    }

    private UnityMaterial Convert(UnityMaterialProperties properties)
    {
        var name = UniqueMaterialName(properties.Name);
        var (blendMode, alphaCutoff) = GetBlendMode(properties);
        var keepsAlpha = blendMode is not MaterialBlendMode.Unspecified;

        var baseTexture = properties.FindTexture("_BaseColorMap", "_UnlitColorMap", "_BaseMap", "_MainTex");
        var tint = baseTexture is null && !properties.HasColor(_tintProperties) && FindCustomColor(properties) is { } custom
            ? custom
            : properties.GetColor(Vector4.One, _tintProperties);
        var diffuse = BuildDiffuse(name, baseTexture?.Texture, tint, keepsAlpha);
        var (uvScale, uvOffset) = baseTexture is { } found ? properties.GetTextureTransform(found.Property) : (Vector2.One, Vector2.Zero);

        var isLit = !properties.ShaderName.Contains("Unlit", StringComparison.OrdinalIgnoreCase);
        var normal = isLit && properties.FindTexture("_NormalMap", "_BumpMap") is { } normalTexture
            ? BuildNormal(normalTexture.Texture)
            : null;
        var specular = isLit && properties.ShaderName.StartsWith("HDRP/", StringComparison.Ordinal)
            ? BuildHdrpSpecular(name, properties)
            : null;
        var emissive = BuildEmissive(name, properties);

        var material = new Material(
            name,
            diffuseTexture: diffuse,
            specularTexture: specular,
            normalTexture: normal,
            emissiveTexture: emissive,
            specularTexturePacking: specular is null ? MaterialSpecularTexturePacking.Standard : MaterialSpecularTexturePacking.SpecularMetallicRoughness,
            normalTextureConvention: MaterialNormalTextureConvention.OpenGl,
            blendMode: blendMode,
            alphaCutoff: alphaCutoff);
        return new UnityMaterial(material, uvScale, uvOffset);
    }

    // Custom shaders (shader graphs) name their colours freely, such as a liquid's _SideColor. The
    // first colour property that reads as a surface colour stands in, opaque, since what its alpha
    // means is up to the shader.
    private static Vector4? FindCustomColor(UnityMaterialProperties properties)
    {
        foreach (var (name, color) in properties.Colors)
        {
            if (!_nonSurfaceColors.Contains(name) &&
                (name.EndsWith("Color", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Tint", StringComparison.OrdinalIgnoreCase)))
            {
                return color with { W = 1f };
            }
        }

        return null;
    }

    private static (MaterialBlendMode Mode, float Cutoff) GetBlendMode(UnityMaterialProperties properties)
    {
        // HDRP: _SurfaceType 1 is transparent, _BlendMode 1 additive; URP: _Surface and _Blend;
        // built-in Standard: _Mode 1 cutout, 2 fade, 3 transparent.
        var cutoff = properties.GetFloat("_AlphaCutoff", properties.GetFloat("_Cutoff", 0.5f));
        var transparent = properties.GetFloat("_SurfaceType", properties.GetFloat("_Surface", 0f)) >= 1f;
        var standardMode = properties.GetFloat("_Mode", 0f);
        if (transparent || standardMode >= 2f)
        {
            var additive = properties.GetFloat("_BlendMode", properties.GetFloat("_Blend", 0f)) is 1f or 2f;
            return (additive ? MaterialBlendMode.Additive : MaterialBlendMode.AlphaBlend, cutoff);
        }

        var alphaTest = properties.GetFloat("_AlphaCutoffEnable", properties.GetFloat("_AlphaClip", 0f)) >= 1f || standardMode is 1f;
        return (alphaTest ? MaterialBlendMode.AlphaTest : MaterialBlendMode.Unspecified, cutoff);
    }

    private Texture BuildDiffuse(string materialName, UnityObjectKey? textureKey, Vector4 tint, bool keepsAlpha)
    {
        var tinted = Vector4.Distance(tint, Vector4.One) > 0.002f;
        var image = textureKey is { } key ? GetImage(key) : null;

        if (image is null)
        {
            return Solid($"{materialName}_color", tint, keepsAlpha);
        }

        if (!tinted)
        {
            return GetTexture(textureKey!.Value, "diffuse", image.Name, () => keepsAlpha ? image.Rgba : Opaque(image.Rgba), image, keepsAlpha);
        }

        var pixels = (byte[])image.Rgba.Clone();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Scale(pixels[i], tint.X);
            pixels[i + 1] = Scale(pixels[i + 1], tint.Y);
            pixels[i + 2] = Scale(pixels[i + 2], tint.Z);
            pixels[i + 3] = keepsAlpha ? Scale(pixels[i + 3], tint.W) : (byte)255;
        }

        return textureFactory.FromRgba(UniqueTextureName($"{materialName}_color"), image.Width, image.Height, pixels, keepsAlpha);
    }

    // Unity reads tangent-space normals as x = R * A, y = G (UnpackNormalmapRGorAG), which covers
    // both DXT5nm (x in alpha, red set to one) and plain RG or RGB layouts (alpha set to one).
    private Texture? BuildNormal(UnityObjectKey key)
    {
        if (GetImage(key) is not { } image)
        {
            return null;
        }

        return GetTexture(key, "normal", image.Name + "_normal", () =>
        {
            var pixels = new byte[image.Rgba.Length];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var x = image.Rgba[i] / 255f * (image.Rgba[i + 3] / 255f) * 2f - 1f;
                var y = image.Rgba[i + 1] / 255f * 2f - 1f;
                var z = MathF.Sqrt(Math.Clamp(1f - x * x - y * y, 0f, 1f));
                pixels[i] = Unit(x * 0.5f + 0.5f);
                pixels[i + 1] = Unit(y * 0.5f + 0.5f);
                pixels[i + 2] = Unit(z * 0.5f + 0.5f);
                pixels[i + 3] = 255;
            }

            return pixels;
        }, image, hasAlpha: false);
    }

    // HDRP's mask map packs R = metallic, G = occlusion, B = detail mask, A = smoothness, each
    // remapped by the material's ranges. Repacked as the SDK's specular/metallic/roughness masks.
    private Texture BuildHdrpSpecular(string materialName, UnityMaterialProperties properties)
    {
        var metallicMin = properties.GetFloat("_MetallicRemapMin", 0f);
        var metallicMax = properties.GetFloat("_MetallicRemapMax", 1f);
        var smoothnessMin = properties.GetFloat("_SmoothnessRemapMin", 0f);
        var smoothnessMax = properties.GetFloat("_SmoothnessRemapMax", 1f);

        if (properties.FindTexture("_MaskMap") is not { } mask || GetImage(mask.Texture) is not { } image)
        {
            var metallic = properties.GetFloat("_Metallic", 0f);
            var smoothness = properties.GetFloat("_Smoothness", 0.5f);
            return Solid($"{materialName}_spec", new Vector4(_defaultSpecular, metallic, 1f - smoothness, 1f), hasAlpha: false);
        }

        var pixels = new byte[image.Rgba.Length];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var metallic = float.Lerp(metallicMin, metallicMax, image.Rgba[i] / 255f);
            var smoothness = float.Lerp(smoothnessMin, smoothnessMax, image.Rgba[i + 3] / 255f);
            pixels[i] = Unit(_defaultSpecular);
            pixels[i + 1] = Unit(metallic);
            pixels[i + 2] = Unit(1f - smoothness);
            pixels[i + 3] = 255;
        }

        return textureFactory.FromRgba(UniqueTextureName($"{materialName}_spec"), image.Width, image.Height, pixels, hasAlpha: false);
    }

    private Texture? BuildEmissive(string materialName, UnityMaterialProperties properties)
    {
        // HDRP reads _EmissiveColor; _EmissionColor is the built-in and URP property, which
        // materials keep at white and only light up with the _EMISSION keyword.
        var color = properties.ShaderName.StartsWith("HDRP/", StringComparison.Ordinal)
            ? properties.GetColor(Vector4.Zero, "_EmissiveColor")
            : properties.HasKeyword("_EMISSION") ? properties.GetColor(Vector4.Zero, "_EmissionColor") : Vector4.Zero;
        var peak = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        if (peak <= 0.001f)
        {
            return null;
        }

        // HDR emission colours carry the intensity; keep the hue and let the texture set the shape.
        var hue = new Vector4(color.X / peak, color.Y / peak, color.Z / peak, 1f);
        if (properties.FindTexture("_EmissiveColorMap", "_EmissionMap") is not { } map || GetImage(map.Texture) is not { } image)
        {
            return Solid($"{materialName}_glow", hue, hasAlpha: false);
        }

        var pixels = new byte[image.Rgba.Length];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Scale(image.Rgba[i], hue.X);
            pixels[i + 1] = Scale(image.Rgba[i + 1], hue.Y);
            pixels[i + 2] = Scale(image.Rgba[i + 2], hue.Z);
            pixels[i + 3] = 255;
        }

        return textureFactory.FromRgba(UniqueTextureName($"{materialName}_glow"), image.Width, image.Height, pixels, hasAlpha: false);
    }

    private UnityTextureImage? GetImage(UnityObjectKey key)
    {
        if (!_images.TryGetValue(key, out var image))
        {
            var texture = library.Find(key);
            image = texture is null ? null : UnityTextureReader.Read(library, texture);
            if (texture is not null && image is null)
            {
                SkippedTextures.Add(texture.Name);
            }

            _images[key] = image;
        }

        return image;
    }

    private Texture GetTexture(UnityObjectKey key, string role, string name, Func<byte[]> pixels, UnityTextureImage image, bool hasAlpha)
    {
        if (!_textures.TryGetValue((key, role), out var texture))
        {
            texture = textureFactory.FromRgba(UniqueTextureName(name), image.Width, image.Height, pixels(), hasAlpha);
            _textures[(key, role)] = texture;
        }

        return texture;
    }

    private Texture Solid(string name, Vector4 color, bool hasAlpha)
    {
        var pixels = new byte[_solidTextureSize * _solidTextureSize * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Unit(color.X);
            pixels[i + 1] = Unit(color.Y);
            pixels[i + 2] = Unit(color.Z);
            pixels[i + 3] = hasAlpha ? Unit(color.W) : (byte)255;
        }

        return textureFactory.FromRgba(UniqueTextureName(name), _solidTextureSize, _solidTextureSize, pixels, hasAlpha);
    }

    private static byte[] Opaque(byte[] rgba)
    {
        var pixels = (byte[])rgba.Clone();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return pixels;
    }

    private string UniqueMaterialName(string name)
    {
        return Unique(_materialNames, string.IsNullOrWhiteSpace(name) ? _defaultMaterialName : name);
    }

    private string UniqueTextureName(string name)
    {
        return Unique(_textureNames, string.IsNullOrWhiteSpace(name) ? "texture" : name);
    }

    private static string Unique(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var suffix = 2; !used.Add(candidate); suffix++)
        {
            candidate = $"{name}_{suffix}";
        }

        return candidate;
    }

    private static byte Scale(byte value, float factor)
    {
        return Unit(value / 255f * factor);
    }

    private static byte Unit(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
    }
}
