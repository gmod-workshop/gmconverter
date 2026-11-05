using System.Numerics;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;
using GMConverter.UnrealEngine.Materials;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// Bakes a UE3 material's outputs into textures by evaluating its expression graph over the base
/// UV square with the instance's parameters, so packed and tinted inputs (two-channel normals,
/// masks feeding roughness and metallic, ambient occlusion multiplied into the colour) come out as
/// the material actually renders them. Outputs the graph can't evaluate fall back to the
/// instance's plain textures.
/// </summary>
internal static class UE3MaterialBaker
{
    private const int _minimumSize = 4;
    private const float _defaultOpacityClip = 1f / 3f;
    private const float _visibleEmission = 1f / 255f;

    public static UE3BakedMaterial Bake(
        UE3GameContent content,
        UE3ObjectReference material,
        int maximumSize,
        Dictionary<string, UE3MaterialTexture?> textureCache)
    {
        var parameters = UE3MaterialParameters.Collect(content, material);
        var name = material.Export.ObjectName;
        if (parameters.BaseMaterial is not { } baseMaterial)
        {
            return Fallback(content, name, parameters);
        }

        var graph = new UE3MaterialGraph(content, parameters, baseMaterial, textureCache);
        var diffuseOutput = graph.CompileOutput("DiffuseColor");
        var normalOutput = graph.CompileOutput("Normal");
        if (diffuseOutput is null && normalOutput is null)
        {
            return Fallback(content, name, parameters);
        }

        var blendMode = graph.Material.GetName("BlendMode");
        var (blend, opacityInput) = blendMode switch
        {
            "BLEND_Masked" => ("masked", "OpacityMask"),
            "BLEND_Translucent" => ("translucent", "Opacity"),
            "BLEND_Additive" => ("additive", null),
            _ => ((string?)null, (string?)null),
        };
        var opacity = opacityInput is null ? null : graph.CompileOutput(opacityInput);
        var alphaCutoff = graph.Material.GetFloat("OpacityMaskClipValue") ?? _defaultOpacityClip;
        if (opacityInput is not null && opacity is null)
        {
            // Without a bakeable opacity the surface is drawn solid rather than cut out at random.
            blend = null;
        }

        // A mask that is solid everywhere (a dissolve effect switched off) needs no alpha at all.
        if (opacity is { Varies: false } && opacity.Evaluate(Vector2.Zero)[0] >= alphaCutoff && blend == "masked")
        {
            opacity = null;
            blend = null;
        }

        var diffuse = diffuseOutput is null
            ? FallbackTexture(content, parameters, "Diffuse", "BaseColor", "Albedo")
            : BakeImage(Combine(diffuseOutput, opacity), maximumSize, (color, alpha) => EncodeColor(color, alpha));
        // Opacity that never drops below the cut-off (or is fully opaque when translucent) doesn't need blending.
        if (blend is "masked" or "translucent" && diffuse is not null && opacity is not null &&
            IsOpaque(diffuse, blend == "masked" ? ToByte(alphaCutoff) : (byte)255))
        {
            blend = null;
            SetOpaque(diffuse);
        }

        var normal = normalOutput is null
            ? FallbackTexture(content, parameters, "Normal")
            : BakeImage([normalOutput], maximumSize, (value, _) => EncodeNormal(value));
        var specular = BakeSpecularMasks(graph, maximumSize);
        var emissiveOutput = graph.CompileOutput("EmissiveColor");
        var emissive = emissiveOutput is not null && (emissiveOutput.Varies || emissiveOutput.Evaluate(Vector2.Zero).Expanded.X > _visibleEmission)
            ? BakeImage([emissiveOutput], maximumSize, (value, _) => EncodeColor(value, null))
            : null;
        if (emissive is not null && IsBlack(emissive))
        {
            emissive = null;
        }

        var baseTexture = (diffuseOutput?.Textures ?? normalOutput?.Textures ?? [])
            .OrderByDescending(texture => texture.Width * texture.Height)
            .FirstOrDefault();
        return new UE3BakedMaterial(
            name,
            diffuse,
            normal,
            specular,
            emissive,
            blend,
            alphaCutoff,
            baseTexture?.AddressX ?? UE3TextureAddress.Wrap,
            baseTexture?.AddressY ?? UE3TextureAddress.Wrap);
    }

    private static UE3MaterialOutput[] Combine(UE3MaterialOutput color, UE3MaterialOutput? opacity)
    {
        return opacity is null ? [color] : [color, opacity];
    }

    private static DecodedImage? BakeSpecularMasks(UE3MaterialGraph graph, int maximumSize)
    {
        var roughness = graph.CompileOutput("Roughness");
        var metallic = graph.CompileOutput("Metallic");
        if (roughness is null && metallic is null)
        {
            return null;
        }

        var specular = graph.CompileOutput("SpecularColor") ?? graph.CompileOutput("Specular");
        UE3MaterialOutput[] outputs = [.. new[] { specular, metallic, roughness }.OfType<UE3MaterialOutput>()];
        var size = GetSize(outputs, maximumSize);
        var pixels = new byte[size.Width * size.Height * 4];
        Parallel.For(0, size.Height, row =>
        {
            for (var column = 0; column < size.Width; column++)
            {
                var uv = new Vector2((column + 0.5f) / size.Width, (row + 0.5f) / size.Height);
                var offset = (row * size.Width + column) * 4;
                pixels[offset] = ToByte(specular?.Evaluate(uv)[0] ?? 0.5f);
                pixels[offset + 1] = ToByte(metallic?.Evaluate(uv)[0] ?? 0f);
                pixels[offset + 2] = ToByte(roughness?.Evaluate(uv)[0] ?? 0.5f);
                pixels[offset + 3] = 255;
            }
        });

        return new DecodedImage(pixels, size.Width, size.Height, DecodedImagePixelFormat.Rgba8888);
    }

    // Outputs[0] is the colour; an optional Outputs[1] supplies alpha.
    private static DecodedImage BakeImage(UE3MaterialOutput[] outputs, int maximumSize, Func<UE3MaterialValue, float?, Vector4> encode)
    {
        var size = GetSize(outputs, maximumSize);
        var pixels = new byte[size.Width * size.Height * 4];
        Parallel.For(0, size.Height, row =>
        {
            for (var column = 0; column < size.Width; column++)
            {
                var uv = new Vector2((column + 0.5f) / size.Width, (row + 0.5f) / size.Height);
                var alpha = outputs.Length > 1 ? outputs[1].Evaluate(uv)[0] : (float?)null;
                var color = encode(outputs[0].Evaluate(uv), alpha);
                var offset = (row * size.Width + column) * 4;
                pixels[offset] = ToByte(color.X);
                pixels[offset + 1] = ToByte(color.Y);
                pixels[offset + 2] = ToByte(color.Z);
                pixels[offset + 3] = ToByte(color.W);
            }
        });

        return new DecodedImage(pixels, size.Width, size.Height, DecodedImagePixelFormat.Rgba8888);
    }

    private static (int Width, int Height) GetSize(IEnumerable<UE3MaterialOutput> outputs, int maximumSize)
    {
        var largest = outputs.SelectMany(output => output.Textures).OrderByDescending(texture => texture.Width * texture.Height).FirstOrDefault();
        if (largest is null)
        {
            return (_minimumSize, _minimumSize);
        }

        var scale = Math.Min(1f, maximumSize / (float)Math.Max(largest.Width, largest.Height));
        return (Math.Max(_minimumSize, (int)(largest.Width * scale)), Math.Max(_minimumSize, (int)(largest.Height * scale)));
    }

    private static Vector4 EncodeColor(UE3MaterialValue value, float? alpha)
    {
        var color = value.Expanded;
        return new Vector4(
            UE3MaterialTexture.LinearToSrgb(Math.Clamp(color.X, 0, 1)),
            UE3MaterialTexture.LinearToSrgb(Math.Clamp(color.Y, 0, 1)),
            UE3MaterialTexture.LinearToSrgb(Math.Clamp(color.Z, 0, 1)),
            alpha ?? 1f);
    }

    private static Vector4 EncodeNormal(UE3MaterialValue value)
    {
        var normal = new Vector3(value[0], value[1], value.Components > 2 ? value[2] : 1f);
        normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        return new Vector4(normal * 0.5f + new Vector3(0.5f), 1f);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    private static bool IsOpaque(DecodedImage image, byte threshold)
    {
        for (var i = 3; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] < threshold)
            {
                return false;
            }
        }

        return true;
    }

    private static void SetOpaque(DecodedImage image)
    {
        for (var i = 3; i < image.Pixels.Length; i += 4)
        {
            image.Pixels[i] = 255;
        }
    }

    private static bool IsBlack(DecodedImage image)
    {
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] > 1 || image.Pixels[i + 1] > 1 || image.Pixels[i + 2] > 1)
            {
                return false;
            }
        }

        return true;
    }

    private static UE3BakedMaterial Fallback(UE3GameContent content, string name, UE3MaterialParameters parameters)
    {
        return new UE3BakedMaterial(
            name,
            FallbackTexture(content, parameters, "Diffuse", "BaseColor", "Albedo"),
            FallbackTexture(content, parameters, "Normal"),
            null,
            null,
            null,
            _defaultOpacityClip,
            UE3TextureAddress.Wrap,
            UE3TextureAddress.Wrap);
    }

    // The instance's texture for a parameter named like the role, decoded as stored.
    private static DecodedImage? FallbackTexture(UE3GameContent content, UE3MaterialParameters parameters, params string[] terms)
    {
        var match = parameters.Textures.FirstOrDefault(pair => terms.Any(term => pair.Key.Contains(term, StringComparison.OrdinalIgnoreCase)));
        if (match.Key is null || !match.Value.ClassName.Equals("Texture2D", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var texture = UE3Texture2D.Read(match.Value.Package, match.Value.Export);
        var image = UE3TextureDecoder.Decode(match.Value.Package, texture, content.TextureCaches);
        return image.IsValid ? image : null;
    }
}
