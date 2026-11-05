using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Materials;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;
using GMConverter.UnrealEngine.Formats.UE3.Packages;
using GMConverter.UnrealEngine.Formats.UE3.Particles;
using GMConverter.UnrealEngine.Formats.UE3.Textures;
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
        List<UE3ParticleSidecar.Emitter> emitters = [];
        foreach (var emitter in system.Emitters)
        {
            var (texture, blend, uv) = (default(string), MaterialBlendMode.AlphaBlend, default(UE3ParticleUvAnimation));
            if (emitter.Material is { } material)
            {
                if (!baked.TryGetValue(material.Key, out var known))
                {
                    known = Bake(content, material, textureCache, outputDirectory);
                    baked[material.Key] = known;
                }

                (texture, blend, uv) = known;
            }

            var settings = emitter.Settings;
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

            emitters.Add(new UE3ParticleSidecar.Emitter(settings, texture, blend, meshFile) { UvScale = uvScale });
        }

        var path = Path.Combine(outputDirectory, NameHelpers.SanitizeFileName(system.Name) + UE3ParticleSidecar.Extension);
        new UE3ParticleSidecar(system.Name, emitters).Write(path);
        return path;
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
