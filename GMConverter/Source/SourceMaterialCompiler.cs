using System.Text;
using GMConverter.Common;
using GMConverter.Geometry;
using GMConverter.SDK.Common;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Textures;

namespace GMConverter.Source;

internal sealed class SourceMaterialCompiler
{
    private readonly string _vtfCmdPath;
    private readonly MaterialOptimizationOptions _optimization;
    private static readonly UTF8Encoding _utf8NoBom = new(false);

    public SourceMaterialCompiler(string vtfCmdPath, MaterialOptimizationOptions? optimization = null)
    {
        _vtfCmdPath = Path.GetFullPath(vtfCmdPath);
        _optimization = optimization ?? MaterialOptimizationOptions.Default;
    }

    public void Compile(IEnumerable<Material> materials, string materialOutputDirectory, string materialRelativeDirectory)
    {
        if (!File.Exists(_vtfCmdPath))
        {
            throw new GMConverterException($"VTFCmd not found: {_vtfCmdPath}");
        }

        var normalizedMaterialDirectory = NormalizeMaterialDirectory(materialRelativeDirectory);
        var tempDirectory = Path.GetFullPath(Path.GetTempPath());
        var materialSourceDirectory = Path.Join(tempDirectory, "GMConverter", "materialsrc", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(materialSourceDirectory);
        Directory.CreateDirectory(materialOutputDirectory);

        // Deduplication maps a (hash, hasAlpha) tuple to the canonical VTF basename already written
        // to disk this run. Two materials that produce identical resized PNGs reference the same
        // VTF instead of paying a second compile+disk write. hasAlpha is part of the key because
        // the same RGB content compiled as DXT5 vs DXT1 produces different VTFs and one VMT may
        // need translucency while the other does not.
        var contentBasenames = new Dictionary<(ulong Hash, bool HasAlpha), string>();

        try
        {
            foreach (var material in materials)
            {
                if (material.DiffuseTexture is null)
                {
                    continue;
                }

                var diffuseBasename = WriteOrReuse(
                    material.DiffuseTexture,
                    material.Name,
                    material.DiffuseTexture.HasAlpha,
                    materialSourceDirectory,
                    materialOutputDirectory,
                    contentBasenames);

                var specForMask = UseSourcePhong(material) ? GetSourcePhongExponent(material) : null;

                string? normalBasename = null;
                if (material.NormalTexture is not null)
                {
                    var normalTextureForWrite = specForMask is not null
                        ? material.NormalTexture.WithMaskInAlpha(specForMask)
                        : material.NormalTexture;
                    normalBasename = WriteOrReuse(
                        normalTextureForWrite,
                        $"{material.Name}_normal",
                        hasAlpha: specForMask is not null,
                        materialSourceDirectory,
                        materialOutputDirectory,
                        contentBasenames);
                }

                string? specBasename = null;
                if (specForMask is not null)
                {
                    // Phong-exponent texture's alpha carries the phong mask (per
                    // ToSourcePhongExponent), so compile as DXT5 to keep that channel intact.
                    specBasename = WriteOrReuse(
                        specForMask,
                        $"{material.Name}_spec",
                        hasAlpha: true,
                        materialSourceDirectory,
                        materialOutputDirectory,
                        contentBasenames);
                }

                string? illumBasename = null;
                if (material.EmissiveTexture is not null)
                {
                    illumBasename = WriteOrReuse(
                        material.EmissiveTexture,
                        $"{material.Name}_illum",
                        material.EmissiveTexture.HasAlpha,
                        materialSourceDirectory,
                        materialOutputDirectory,
                        contentBasenames);
                }

                var vmtPath = Path.Join(materialOutputDirectory, GetFileNameOnly($"{material.Name}.vmt"));
                WriteVmt(
                    vmtPath,
                    $"{normalizedMaterialDirectory}/{diffuseBasename}",
                    normalBasename is null ? null : $"{normalizedMaterialDirectory}/{normalBasename}",
                    specBasename is null ? null : $"{normalizedMaterialDirectory}/{specBasename}",
                    illumBasename is null ? null : $"{normalizedMaterialDirectory}/{illumBasename}",
                    material);
            }
        }
        finally
        {
            if (Directory.Exists(materialSourceDirectory))
            {
                Directory.Delete(materialSourceDirectory, recursive: true);
            }
        }
    }

    private string WriteOrReuse(
        Texture texture,
        string preferredBasename,
        bool hasAlpha,
        string materialSourceDirectory,
        string materialOutputDirectory,
        Dictionary<(ulong Hash, bool HasAlpha), string> contentBasenames)
    {
        var resized = _optimization.MaxTextureSize > 0
            ? texture.Resized(_optimization.MaxTextureSize)
            : texture;

        if (_optimization.DeduplicateTextures)
        {
            var key = (resized.ContentHash(), hasAlpha);
            if (contentBasenames.TryGetValue(key, out var existing))
            {
                return existing;
            }

            contentBasenames[key] = preferredBasename;
        }

        var sourcePath = GetSourceTexturePath(materialSourceDirectory, preferredBasename);
        resized.WritePng(sourcePath);
        RunVtfCmd(sourcePath, materialOutputDirectory);
        return preferredBasename;
    }

    // `-resize` snaps non-power-of-two inputs to the nearest POT. Multi-layer-baked materials
    // (MultiLayerBaker produces baseWidth*tileX × baseHeight*tileY) can land on non-POT dimensions
    // when tileX/tileY aren't powers of two, and after our MaxTextureSize cap they often still
    // aren't POT. Without -resize VtfCmd silently exits 0 without producing a VTF on those inputs,
    // which surfaces in-engine as the missing-texture checker. Format flags are intentionally
    // omitted so VtfCmd auto-picks DXT1/DXT5 from the actual PNG alpha — forcing DXT1 on alpha-
    // bearing sources can trigger the same silent-no-output failure mode.
    private void RunVtfCmd(string sourcePath, string outputDirectory)
    {
        ProcessRunner.Run(
            _vtfCmdPath,
            ["-file", sourcePath, "-output", outputDirectory, "-resize", "-silent"],
            Path.GetDirectoryName(_vtfCmdPath));

        // Wrap with Path.GetFileName so the second arg is unambiguously a leaf name and
        // Path.Combine can't drop outputDirectory if a future caller passes a rooted sourcePath.
        var expectedVtfName = Path.GetFileName(Path.GetFileNameWithoutExtension(sourcePath) + ".vtf");
        var expectedVtfPath = Path.Combine(outputDirectory, expectedVtfName);
        if (!File.Exists(expectedVtfPath))
        {
            throw new GMConverterException(
                $"VTFCmd exited successfully but did not produce {expectedVtfPath} (source: {sourcePath}).");
        }
    }

    private static void WriteVmt(
        string vmtPath,
        string baseTexturePath,
        string? normalTexturePath,
        string? specTexturePath,
        string? illumTexturePath,
        Material material)
    {
        using var writer = new StreamWriter(vmtPath, false, _utf8NoBom);
        writer.WriteLine("\"VertexLitGeneric\"");
        writer.WriteLine("{");
        writer.WriteLine(FormattableString.Invariant($"    \"$basetexture\" \"{baseTexturePath}\""));
        writer.WriteLine("    \"$nocull\" \"1\"");
        WriteSurfaceProp(writer, material);

        if (normalTexturePath is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$bumpmap\" \"{normalTexturePath}\""));
        }

        if (material.HasAlpha)
        {
            writer.WriteLine("    \"$translucent\" \"1\"");
        }

        if (specTexturePath is not null && UseSourcePhong(material))
        {
            WritePhongParameters(writer, specTexturePath, material);
            WriteEnvmapParameters(
                writer,
                standaloneMaskPath: normalTexturePath is null ? specTexturePath : null,
                normalMapAlphaMask: normalTexturePath is not null);
        }

        if (material.IsIlluminated && illumTexturePath is not null)
        {
            writer.WriteLine("    \"$selfillum\" \"1\"");
            writer.WriteLine(FormattableString.Invariant($"    \"$selfillummask\" \"{illumTexturePath}\""));
        }

        writer.WriteLine("}");
    }

    private static void WriteSurfaceProp(StreamWriter writer, Material material)
    {
        var surfaceProp = SourceMaterialSurfaceProps.For(material);
        if (surfaceProp is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$surfaceprop\" \"{surfaceProp}\""));
        }
    }

    private static bool UseSourcePhong(Material material)
    {
        return material.DiffuseTexture is not null && material.SpecularTexture is not null;
    }

    private static Texture? GetSourcePhongExponent(Material material)
    {
        return material.SpecularTexturePacking == MaterialSpecularTexturePacking.UnrealSpecularMasks
            ? material.SpecularTexture?.ToSourcePhongExponent()
            : material.SpecularTexture;
    }

    private static void WritePhongParameters(StreamWriter writer, string specularTexturePath, Material material)
    {
        var settings = SourcePhongSettings.For(material);

        writer.WriteLine("    \"$phong\" \"1\"");
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponenttexture\" \"{specularTexturePath}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongboost\" \"{settings.Boost}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponent\" \"{settings.Exponent}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongfresnelranges\" \"{settings.FresnelRanges}\""));
    }

    // See MDLExporter.WriteEnvmapParameters for rationale.
    private static void WriteEnvmapParameters(
        StreamWriter writer,
        string? standaloneMaskPath = null,
        bool normalMapAlphaMask = false)
    {
        writer.WriteLine("    \"$envmap\" \"env_cubemap\"");
        writer.WriteLine("    \"$envmapfresnel\" \"1\"");
        writer.WriteLine("    \"$envmaptint\" \"[0.5 0.5 0.5]\"");
        if (normalMapAlphaMask)
        {
            writer.WriteLine("    \"$normalmapalphaenvmapmask\" \"1\"");
        }
        else if (standaloneMaskPath is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$envmapmask\" \"{standaloneMaskPath}\""));
        }
    }

    private static string GetSourceTexturePath(string materialSourceDirectory, string textureName)
    {
        string fileName = GetFileNameOnly($"{textureName}.png");
        return Path.Join(materialSourceDirectory, fileName);
    }

    private static string GetFileNameOnly(string path)
    {
        string? fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            throw new GMConverterException($"Invalid material file name: {path}");
        }

        return fileName;
    }

    private static string NormalizeMaterialDirectory(string materialRelativeDirectory)
    {
        var normalized = materialRelativeDirectory.Replace('\\', '/').Trim('/');

        if (normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["materials/".Length..];
        }

        return string.IsNullOrWhiteSpace(normalized) ? "gmconverter" : normalized;
    }
}
