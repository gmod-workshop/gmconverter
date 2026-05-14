using System.Text;
using GMConverter.Common;
using GMConverter.Geometry;

namespace GMConverter.Source;

internal sealed class SourceMaterialCompiler(string vtfCmdPath)
{
    private readonly string _vtfCmdPath = Path.GetFullPath(vtfCmdPath);
    private static readonly UTF8Encoding _utf8NoBom = new(false);

    public void Compile(IEnumerable<Material> materials, string materialOutputDirectory, string materialRelativeDirectory)
    {
        if (!File.Exists(_vtfCmdPath))
        {
            throw new GMConverterException($"VTFCmd not found: {_vtfCmdPath}");
        }

        var normalizedMaterialDirectory = NormalizeMaterialDirectory(materialRelativeDirectory);
        var materialSourceDirectory = Path.Combine(Path.GetTempPath(), "GMConverter", "materialsrc", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(materialSourceDirectory);
        Directory.CreateDirectory(materialOutputDirectory);

        foreach (var material in materials)
        {
            if (material.DiffuseTexture is null)
            {
                continue;
            }

            var baseTexturePath = $"{normalizedMaterialDirectory}/{material.Name}".Replace('\\', '/');
            var texturePath = Path.Combine(materialSourceDirectory, $"{material.Name}.png");
            var vmtPath = Path.Combine(materialOutputDirectory, $"{material.Name}.vmt");

            if (UseSourcePhong(material))
            {
                material.DiffuseTexture.WritePng(texturePath, material.SpecularTexture!);
            }
            else
            {
                material.DiffuseTexture.WritePng(texturePath);
            }

            RunVtfCmd(texturePath, materialOutputDirectory);

            if (material.NormalTexture is not null)
            {
                var normalName = $"{material.Name}_normal";
                var normalPath = Path.Combine(materialSourceDirectory, $"{normalName}.png");

                material.NormalTexture.WritePng(normalPath);
                RunVtfCmd(normalPath, materialOutputDirectory);
            }

            if (UseSourcePhong(material))
            {
                var specularName = $"{material.Name}_spec";
                var specularPath = Path.Combine(materialSourceDirectory, $"{specularName}.png");

                material.SpecularTexture!.WritePng(specularPath);
                RunVtfCmd(specularPath, materialOutputDirectory);
            }

            WriteVmt(vmtPath, baseTexturePath, material);

            if (material.EmissiveTexture is not null)
            {
                var illumName = $"{material.Name}_illum";
                var illumPath = Path.Combine(materialSourceDirectory, $"{illumName}.png");

                material.EmissiveTexture.WritePng(illumPath);
                RunVtfCmd(illumPath, materialOutputDirectory);
            }
        }

        Directory.Delete(materialSourceDirectory, recursive: true);
    }

    private void RunVtfCmd(string sourcePath, string outputDirectory)
    {
        ProcessRunner.Run(
            _vtfCmdPath,
            ["-file", sourcePath, "-output", outputDirectory, "-silent"],
            Path.GetDirectoryName(_vtfCmdPath));
    }

    private static void WriteVmt(string vmtPath, string baseTexturePath, Material material)
    {
        using var writer = new StreamWriter(vmtPath, false, _utf8NoBom);
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
