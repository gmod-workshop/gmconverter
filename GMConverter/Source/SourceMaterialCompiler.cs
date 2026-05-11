using System.Text;
using GMConverter.Common;
using GMConverter.Geometry;

namespace GMConverter.Source;

internal sealed class SourceMaterialCompiler(string vtexPath, string gameDirectory)
{
    private readonly string vtexPath = Path.GetFullPath(vtexPath);
    private readonly string gameDirectory = Path.GetFullPath(gameDirectory);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public void Compile(IEnumerable<Material> materials, string materialRelativeDirectory)
    {
        if (!File.Exists(vtexPath))
        {
            throw new GMConverterException($"vtex not found: {vtexPath}");
        }

        if (!Directory.Exists(gameDirectory))
        {
            throw new GMConverterException($"Game directory not found: {gameDirectory}");
        }

        var normalizedMaterialDirectory = NormalizeMaterialDirectory(materialRelativeDirectory);
        var materialSourceDirectory = Path.Combine(gameDirectory, "materialsrc", normalizedMaterialDirectory.Replace('/', Path.DirectorySeparatorChar));
        var materialOutputDirectory = Path.Combine(gameDirectory, "materials", normalizedMaterialDirectory.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(materialSourceDirectory);
        Directory.CreateDirectory(materialOutputDirectory);

        foreach (var material in materials)
        {
            if (material.DiffuseTexture is null)
            {
                continue;
            }

            var baseTexturePath = $"{normalizedMaterialDirectory}/{material.Name}".Replace('\\', '/');
            var tgaPath = Path.Combine(materialSourceDirectory, $"{material.Name}.tga");
            var vmtPath = Path.Combine(materialOutputDirectory, $"{material.Name}.vmt");

            if (UseSourcePhong(material))
            {
                material.DiffuseTexture.WriteTga(tgaPath, material.SpecularTexture!);
            }
            else
            {
                material.DiffuseTexture.WriteTga(tgaPath);
            }

            RunVtex(tgaPath);

            if (material.NormalTexture is not null)
            {
                var normalName = $"{material.Name}_normal";
                var normalPath = Path.Combine(materialSourceDirectory, $"{normalName}.tga");

                material.NormalTexture.WriteTga(normalPath);
                RunVtex(normalPath);
            }

            if (UseSourcePhong(material))
            {
                var specularName = $"{material.Name}_spec";
                var specularPath = Path.Combine(materialSourceDirectory, $"{specularName}.tga");

                material.SpecularTexture!.WriteTga(specularPath);
                RunVtex(specularPath);
            }

            WriteVmt(vmtPath, baseTexturePath, material);

            if (material.EmissiveTexture is not null)
            {
                var illumName = $"{material.Name}_illum";
                var illumPath = Path.Combine(materialSourceDirectory, $"{illumName}.tga");

                material.EmissiveTexture.WriteTga(illumPath);
                RunVtex(illumPath);
            }
        }
    }

    private void RunVtex(string tgaPath)
    {
        ProcessRunner.Run(vtexPath, ["-nopause", "-mkdir", tgaPath], Path.GetDirectoryName(vtexPath));
    }

    private void WriteVmt(string vmtPath, string baseTexturePath, Material material)
    {
        using var writer = new StreamWriter(vmtPath, false, Utf8NoBom);
        writer.WriteLine("\"VertexLitGeneric\"");
        writer.WriteLine("{");
        writer.WriteLine(FormattableString.Invariant($"    \"$basetexture\" \"{baseTexturePath}\""));
        writer.WriteLine("    \"$nocull\" \"1\"");
        WriteSurfaceProp(writer, material);

        if (material.NormalTexture is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$bumpmap\" \"{baseTexturePath}_normal\""));
        }

        if (UseSourcePhong(material))
        {
            WritePhongParameters(writer, $"{baseTexturePath}_spec", material);
        }
        else if (material.HasAlpha)
        {
            writer.WriteLine("    \"$translucent\" \"1\"");
        }

        if (material.IsIlluminated)
        {
            writer.WriteLine("    \"$selfillum\" \"1\"");
            writer.WriteLine(FormattableString.Invariant($"    \"$selfillummask\" \"{baseTexturePath}_illum\""));
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
        return material.DiffuseTexture is not null &&
            material.SpecularTexture is not null &&
            !material.HasAlpha;
    }

    private static void WritePhongParameters(StreamWriter writer, string specularTexturePath, Material material)
    {
        var settings = SourcePhongSettings.For(material);

        writer.WriteLine("    \"$phong\" \"1\"");
        writer.WriteLine("    \"$basemapalphaphongmask\" \"1\"");
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponenttexture\" \"{specularTexturePath}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongboost\" \"{settings.Boost}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponent\" \"{settings.Exponent}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongfresnelranges\" \"{settings.FresnelRanges}\""));
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
