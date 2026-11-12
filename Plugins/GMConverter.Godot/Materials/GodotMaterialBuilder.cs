using System.Numerics;
using GMConverter.Godot.Packs;
using GMConverter.Godot.Resources;
using GMConverter.Godot.Textures;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;

namespace GMConverter.Godot.Materials;

/// <summary>
/// Converts Godot materials for one import. Reads <c>StandardMaterial3D</c> and
/// <c>ORMMaterial3D</c>: albedo texture and colour, normal map, roughness and metallic, emission,
/// transparency and texture scale. Custom shaders (<c>ShaderMaterial</c>) fall back to a plain
/// material. Materials and textures are shared across the import and named uniquely.
/// </summary>
internal sealed class GodotMaterialBuilder(GodotResourceLoader loader, ITextureFactory textureFactory)
{
    private const string _defaultMaterialName = "default";
    private const float _defaultSpecular = 0.5f;
    private const int _solidTextureSize = 4;

    // StandardMaterial3D's transparency and blend mode values.
    private const long _transparencyAlpha = 1;
    private const long _transparencyAlphaScissor = 2;
    private const long _transparencyAlphaHash = 3;
    private const long _transparencyDepthPrepass = 4;
    private const long _blendAdd = 1;
    private const long _shadingUnshaded = 0;

    private readonly Dictionary<GodotResource, GodotMaterial> _materials = new(ReferenceEqualityComparer.Instance);
    private readonly List<GodotMaterial> _converted = [];
    private readonly Dictionary<string, GodotTextureImage?> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), Texture> _textures = [];
    private readonly HashSet<string> _materialNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _textureNames = new(StringComparer.OrdinalIgnoreCase);
    private GodotMaterial? _defaultMaterial;

    /// <summary>Every material converted so far, in the order first used.</summary>
    public IReadOnlyList<GodotMaterial> Converted => _converted;

    /// <summary>Textures that could not be read or decoded.</summary>
    public List<string> SkippedTextures { get; } = [];

    /// <summary>The converted material, or a plain default one for a missing material.</summary>
    public GodotMaterial Build(GodotResource? material)
    {
        if (material is null)
        {
            if (_defaultMaterial is null)
            {
                _defaultMaterial = new GodotMaterial(new Material(UniqueMaterialName(_defaultMaterialName)), Vector2.One, Vector2.Zero);
                _converted.Add(_defaultMaterial);
            }

            return _defaultMaterial;
        }

        if (!_materials.TryGetValue(material, out var converted))
        {
            converted = material.Type is "StandardMaterial3D" or "ORMMaterial3D"
                ? Convert(material)
                : new GodotMaterial(new Material(UniqueMaterialName(Name(material))), Vector2.One, Vector2.Zero);
            _materials[material] = converted;
            _converted.Add(converted);
        }

        return converted;
    }

    private GodotMaterial Convert(GodotResource material)
    {
        var name = UniqueMaterialName(Name(material));
        var transparency = material["transparency"] is long mode ? mode : 0;
        var additive = material["blend_mode"] is long blend && blend == _blendAdd;
        var blendMode = additive
            ? MaterialBlendMode.Additive
            : transparency switch
            {
                _transparencyAlpha or _transparencyDepthPrepass => MaterialBlendMode.AlphaBlend,
                _transparencyAlphaScissor or _transparencyAlphaHash => MaterialBlendMode.AlphaTest,
                _ => MaterialBlendMode.Opaque,
            };
        var keepsAlpha = blendMode is MaterialBlendMode.AlphaBlend or MaterialBlendMode.AlphaTest;

        var tint = Color(material["albedo_color"], Vector4.One);
        var diffuse = BuildDiffuse(name, material["albedo_texture"] as GodotExternalResource, tint, keepsAlpha);
        var unshaded = material["shading_mode"] is long shading && shading == _shadingUnshaded;
        var normal = !unshaded && material["normal_enabled"] is true && material["normal_texture"] is GodotExternalResource normalTexture
            ? BuildNormal(normalTexture)
            : null;
        var specular = unshaded ? null : BuildSpecular(name, material);

        // An unshaded material shows its colour whatever the light, like a texture that glows.
        var emissive = unshaded ? diffuse : BuildEmissive(name, material);

        var uvScale = material["uv1_scale"] is Vector3 scale ? new Vector2(scale.X, scale.Y) : Vector2.One;
        var uvOffset = material["uv1_offset"] is Vector3 offset ? new Vector2(offset.X, offset.Y) : Vector2.Zero;
        return new GodotMaterial(
            new Material(
                name,
                diffuseTexture: diffuse,
                specularTexture: specular,
                normalTexture: normal,
                emissiveTexture: emissive,
                specularTexturePacking: specular is null ? MaterialSpecularTexturePacking.Standard : MaterialSpecularTexturePacking.SpecularMetallicRoughness,
                normalTextureConvention: MaterialNormalTextureConvention.OpenGl,
                blendMode: blendMode,
                alphaCutoff: material["alpha_scissor_threshold"] is double cutoff ? (float)cutoff : 0.5f),
            uvScale,
            uvOffset);
    }

    private Texture BuildDiffuse(string materialName, GodotExternalResource? textureReference, Vector4 tint, bool keepsAlpha)
    {
        var image = textureReference is null ? null : GetImage(textureReference);
        if (image is null)
        {
            return Solid($"{materialName}_color", tint, keepsAlpha);
        }

        if (Vector4.Distance(tint, Vector4.One) <= 0.002f)
        {
            return GetTexture(textureReference!.Path, "diffuse", image.Name, () => keepsAlpha ? image.Rgba : Opaque(image.Rgba), image, keepsAlpha);
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

    // Godot compresses normal maps to their X and Y; Z is rebuilt. Other normal maps are plain RGB.
    private Texture? BuildNormal(GodotExternalResource textureReference)
    {
        if (GetImage(textureReference) is not { } image)
        {
            return null;
        }

        return GetTexture(textureReference.Path, "normal", image.Name + "_normal", () =>
        {
            var pixels = (byte[])image.Rgba.Clone();
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (image.IsNormalMap)
                {
                    var x = (pixels[i] / 255f * 2f) - 1f;
                    var y = (pixels[i + 1] / 255f * 2f) - 1f;
                    pixels[i + 2] = Unit((MathF.Sqrt(Math.Clamp(1f - (x * x) - (y * y), 0f, 1f)) * 0.5f) + 0.5f);
                }

                pixels[i + 3] = 255;
            }

            return pixels;
        }, image, hasAlpha: false);
    }

    // Godot's roughness and metallic values, as the SDK's specular/metallic/roughness masks.
    private Texture BuildSpecular(string materialName, GodotResource material)
    {
        var roughness = material["roughness"] is double rough ? (float)rough : 1f;
        var metallic = material["metallic"] is double metal ? (float)metal : 0f;
        var specular = material["metallic_specular"] is double spec ? (float)spec : _defaultSpecular;
        return Solid($"{materialName}_spec", new Vector4(specular, metallic, roughness, 1f), hasAlpha: false);
    }

    private Texture? BuildEmissive(string materialName, GodotResource material)
    {
        if (material["emission_enabled"] is not true)
        {
            return null;
        }

        var color = Color(material["emission"], new Vector4(0f, 0f, 0f, 1f));
        var peak = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        if (peak <= 0.001f)
        {
            return null;
        }

        var hue = new Vector4(color.X / peak, color.Y / peak, color.Z / peak, 1f);
        if (material["emission_texture"] is not GodotExternalResource map || GetImage(map) is not { } image)
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

    private GodotTextureImage? GetImage(GodotExternalResource texture)
    {
        if (!_images.TryGetValue(texture.Path, out var image))
        {
            var name = Path.GetFileNameWithoutExtension(texture.Path);
            image = loader.PathOf(texture) is { } path ? GodotTextureReader.Read(loader.ReadBytes(path), name, textureFactory) : null;
            if (image is null)
            {
                SkippedTextures.Add(name);
            }

            _images[texture.Path] = image;
        }

        return image;
    }

    private Texture GetTexture(string path, string role, string name, Func<byte[]> pixels, GodotTextureImage image, bool hasAlpha)
    {
        if (!_textures.TryGetValue((path, role), out var texture))
        {
            texture = textureFactory.FromRgba(UniqueTextureName(name), image.Width, image.Height, pixels(), hasAlpha);
            _textures[(path, role)] = texture;
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

    private static string Name(GodotResource material)
    {
        return material["resource_name"] is string { Length: > 0 } name ? name : _defaultMaterialName;
    }

    private static Vector4 Color(object? value, Vector4 fallback)
    {
        return value is GodotColor color ? new Vector4(color.R, color.G, color.B, color.A) : fallback;
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
