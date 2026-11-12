using System.Globalization;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Textures;
using GMConverter.UnrealEngine.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>
/// Bakes a UE3 mesh's materials and writes each as PNG textures plus a <c>.mat</c> sidecar the PSK
/// importer reads. The textures are already in plain roles, so the sidecar says how they are
/// packed rather than leaving the importer to guess from names.
/// </summary>
internal static class UE3MaterialExporter
{
    /// <summary>Sidecar key naming the specular mask's packing (a <see cref="SDK.Materials.MaterialSpecularTexturePacking"/> name).</summary>
    public const string SpecularPackingKey = "SpecularPacking";

    /// <summary>Sidecar key naming the normal map's green-channel convention (a <see cref="SDK.Materials.MaterialNormalTextureConvention"/> name).</summary>
    public const string NormalConventionKey = "NormalConvention";

    /// <summary>Sidecar key naming the material's translated graph (a <see cref="UE3MaterialGraphFile"/> beside it), for lit materials that translate.</summary>
    public const string GraphKey = "Graph";

    private const string _noMaterial = "None";

    /// <summary>Writes every material slot, returning each slot's name and how its base texture repeats.</summary>
    public static IReadOnlyList<(string Name, UE3TextureAddress AddressX, UE3TextureAddress AddressY)> Export(
        UE3GameContent content,
        UE3Package package,
        IReadOnlyList<int> materials,
        string outputDirectory,
        int maximumTextureSize)
    {
        Dictionary<string, UE3MaterialTexture?> textureCache = [];
        Dictionary<string, UE3MaterialGraphFile.TextureFile> graphTextures = new(StringComparer.OrdinalIgnoreCase);
        List<(string, UE3TextureAddress, UE3TextureAddress)> slots = [];
        for (var slot = 0; slot < Math.Max(materials.Count, 1); slot++)
        {
            var reference = slot < materials.Count ? content.Resolve(package, materials[slot]) : null;
            if (reference is not { } found)
            {
                slots.Add((slot < materials.Count ? package.GetObjectName(materials[slot]) : _noMaterial, UE3TextureAddress.Wrap, UE3TextureAddress.Wrap));
                continue;
            }

            var material = new UE3ObjectReference(found.Package, found.Export);
            var baked = UE3MaterialBaker.Bake(content, material, maximumTextureSize, textureCache);
            WriteMaterial(baked, outputDirectory, Graph(content, material, baked.Name, outputDirectory, graphTextures));
            slots.Add((baked.Name, baked.AddressX, baked.AddressY));
        }

        return slots;
    }

    // A lit material's graph, written beside its sidecar; null, logged, when it doesn't translate.
    private static string? Graph(UE3GameContent content, UE3ObjectReference material, string name, string outputDirectory, Dictionary<string, UE3MaterialGraphFile.TextureFile> written)
    {
        var translated = UE3MaterialTranslator.Translate(content, material, out var failure);
        if (translated is not { Surface: not null })
        {
            PerfTimer.Log("ue3.materials", $"{name} keeps its baked textures only: {failure ?? "it is unlit"}");
            return null;
        }

        var file = name + UE3MaterialGraphFile.Extension;
        UE3MaterialGraphFile.Export(content, translated, outputDirectory, written).Write(Path.Combine(outputDirectory, file));
        return file;
    }

    private static void WriteMaterial(UE3BakedMaterial material, string outputDirectory, string? graph)
    {
        List<string> lines = [];
        if (graph is not null)
        {
            lines.Add($"{GraphKey}={graph}");
        }

        string? Save(DecodedImage? image, string role)
        {
            if (image is null)
            {
                return null;
            }

            var name = $"{material.Name}_{role}";
            using var png = Image.LoadPixelData<Rgba32>(image.Pixels, image.Width, image.Height);
            png.SaveAsPng(Path.Combine(outputDirectory, name + ".png"));
            return name;
        }

        if (Save(material.Diffuse, "diffuse") is { } diffuse)
        {
            lines.Add($"Diffuse={diffuse}");
            if (material.Blend is "masked" or "translucent")
            {
                lines.Add($"Opacity={diffuse}");
            }
        }

        if (Save(material.Normal, "normal") is { } normal)
        {
            lines.Add($"Normal={normal}");
            lines.Add($"{NormalConventionKey}=DirectX");
        }

        if (Save(material.SpecularMasks, "specular") is { } specular)
        {
            lines.Add($"SpecularMasks={specular}");
            lines.Add($"{SpecularPackingKey}=SpecularMetallicRoughness");
        }

        if (Save(material.Emissive, "emissive") is { } emissive)
        {
            lines.Add($"Emissive={emissive}");
        }

        if (material.Blend is { } blend)
        {
            lines.Add($"Blend={blend}");
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"AlphaRef={(int)MathF.Round(material.AlphaCutoff * 255f)}"));
        }

        File.WriteAllLines(Path.Combine(outputDirectory, material.Name + ".mat"), lines);
    }
}
