using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Textures;
using GMConverter.UnrealEngine.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>
/// Writes a Cascade particle system as a <see cref="UE3ParticleSidecar"/>, with each emitter's
/// material baked to one texture: the glow for additive materials, colour with opacity in alpha
/// for translucent ones.
/// </summary>
internal static class UE3ParticleExporter
{
    private const int _maximumTextureSize = 512;

    public static string Export(UE3GameContent content, UE3Package package, UE3PackageExport export, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var system = UE3ParticleSystem.Read(content, package, export);
        if (system.Skipped.Count > 0)
        {
            PerfTimer.Log("ue3.particles", $"{system.Name}: modules not converted: {string.Join(", ", system.Skipped)}");
        }

        Dictionary<string, UE3MaterialTexture?> textureCache = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (string? Texture, MaterialBlendMode Blend, UE3ParticleUvAnimation? Uv)> baked = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> meshes = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, UE3ParticleSidecar.GraphFile?> graphs = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, UE3ParticleSidecar.GraphTexture> graphTextures = new(StringComparer.OrdinalIgnoreCase);
        List<UE3ParticleSidecar.Emitter> emitters = [];
        foreach (var emitter in system.Emitters)
        {
            var (texture, blend, uv) = (default(string), MaterialBlendMode.AlphaBlend, default(UE3ParticleUvAnimation));
            UE3ParticleSidecar.GraphFile? graph = null;
            if (emitter.Material is { } material)
            {
                if (!baked.TryGetValue(material.Key, out var known))
                {
                    known = Bake(content, material, textureCache, outputDirectory);
                    baked[material.Key] = known;
                }

                (texture, blend, uv) = known;
                if (!graphs.TryGetValue(material.Key, out graph))
                {
                    graph = Translate(content, material, graphTextures, outputDirectory, system.Name);
                    graphs[material.Key] = graph;
                }
            }

            var settings = emitter.Settings with { Parameters = emitter.DynamicParameters };
            Vector2? uvScale = null;
            // The material slides its texture by a dynamic parameter: speed times the parameter's curve.
            if (uv is not null && emitter.DynamicParameters[uv.Channel] is { } parameter)
            {
                ParticleVectorKey Offset(ParticleKey key) => new(key.Time, new Vector3(uv.Speed * key.Value, 0f));
                settings = settings with { UvOffsetOverLife = new ParticleVector([.. parameter.Min.Select(Offset)], [.. parameter.Max.Select(Offset)]) };
                uvScale = uv.Tiling;
            }

            string? meshFile = null;
            if (emitter.Mesh is { } mesh && !meshes.TryGetValue(mesh.Key, out meshFile))
            {
                meshFile = NameHelpers.SanitizeFileName(mesh.Export.ObjectName) + ".psk";
                var skeletal = UE3StaticMesh.Read(mesh.Package, mesh.Export).ToSkeletalMesh();
                UE3PskWriter.Write(Path.Combine(outputDirectory, meshFile), skeletal,
                    [.. skeletal.Materials.Select(_ => "particle")], [.. skeletal.Materials.Select(_ => (UE3TextureAddress.Wrap, UE3TextureAddress.Wrap))]);
                meshes[mesh.Key] = meshFile;
            }

            emitters.Add(new UE3ParticleSidecar.Emitter(settings, texture, blend, meshFile) { UvScale = uvScale, Graph = graph });
        }

        var path = Path.Combine(outputDirectory, NameHelpers.SanitizeFileName(system.Name) + UE3ParticleSidecar.Extension);
        new UE3ParticleSidecar(system.Name, emitters).Write(path);
        return path;
    }

    // The material's graph with each texture it samples written once, as stored (not baked); null, logged, when it doesn't translate.
    private static UE3ParticleSidecar.GraphFile? Translate(
        UE3GameContent content, UE3ObjectReference material, Dictionary<string, UE3ParticleSidecar.GraphTexture> written, string outputDirectory, string systemName)
    {
        List<UE3ParticleSidecar.GraphTexture> textures = [];
        Dictionary<string, int> indices = new(StringComparer.OrdinalIgnoreCase);
        int TextureIndex(UE3ObjectReference reference)
        {
            if (indices.TryGetValue(reference.Key, out var index))
            {
                return index;
            }

            if (!written.TryGetValue(reference.Key, out var file))
            {
                file = WriteTexture(content, reference, outputDirectory, written.Values);
                written[reference.Key] = file;
            }

            textures.Add(file);
            indices[reference.Key] = textures.Count - 1;
            return textures.Count - 1;
        }

        if (UE3MaterialTranslator.Translate(content, material, TextureIndex, out var failure) is not { } translated)
        {
            PerfTimer.Log("ue3.particles", $"{systemName}: material {material.Export.ObjectName} keeps its baked texture: {failure}");
            return null;
        }

        return new UE3ParticleSidecar.GraphFile(translated.Nodes, textures, translated.Emissive, translated.Opacity);
    }

    private static UE3ParticleSidecar.GraphTexture WriteTexture(
        UE3GameContent content, UE3ObjectReference reference, string outputDirectory, IEnumerable<UE3ParticleSidecar.GraphTexture> taken)
    {
        var source = UE3Texture2D.Read(reference.Package, reference.Export);
        var image = UE3TextureDecoder.Decode(reference.Package, source, content.TextureCaches);
        if (!image.IsValid)
        {
            throw new GMConverterException($"UE3 texture {source.Name} could not be decoded.");
        }

        var stem = NameHelpers.SanitizeFileName(source.Name);
        var file = stem + ".png";
        for (var suffix = 2; taken.Any(texture => texture.File.Equals(file, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            file = FormattableString.Invariant($"{stem}_{suffix}.png");
        }

        if (image.Format == DecodedImagePixelFormat.Bgra8888)
        {
            using var bgra = Image.LoadPixelData<Bgra32>(image.Pixels, image.Width, image.Height);
            bgra.SaveAsPng(Path.Combine(outputDirectory, file));
        }
        else
        {
            using var rgba = Image.LoadPixelData<Rgba32>(image.Pixels, image.Width, image.Height);
            rgba.SaveAsPng(Path.Combine(outputDirectory, file));
        }

        return new UE3ParticleSidecar.GraphTexture(file, source.Srgb, source.AddressX != UE3TextureAddress.Wrap, source.AddressY != UE3TextureAddress.Wrap);
    }

    private static (string? Texture, MaterialBlendMode Blend, UE3ParticleUvAnimation? Uv) Bake(
        UE3GameContent content, UE3ObjectReference material, Dictionary<string, UE3MaterialTexture?> textureCache, string outputDirectory)
    {
        var baked = UE3MaterialBaker.Bake(content, material, _maximumTextureSize, textureCache, particleTextureSpace: true);
        var additive = baked.Blend == "additive";
        var image = additive ? baked.Emissive ?? baked.Diffuse : baked.Diffuse ?? baked.Emissive;
        if (image is not { IsValid: true })
        {
            return (null, additive ? MaterialBlendMode.Additive : MaterialBlendMode.AlphaBlend, baked.ParticleUv);
        }

        var file = NameHelpers.SanitizeFileName(baked.Name) + ".png";
        using var png = Image.LoadPixelData<Rgba32>(image.Pixels, image.Width, image.Height);
        png.SaveAsPng(Path.Combine(outputDirectory, file));
        return (file, additive ? MaterialBlendMode.Additive : MaterialBlendMode.AlphaBlend, baked.ParticleUv);
    }
}
