using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Unreal;

internal static class UnrealTextureExporter
{
    private const int _texfDxt1 = 3;
    private const int _texfDxt3 = 7;
    private const int _texfDxt5 = 8;

    public static UnrealExportedTexture? ExportTexture(UnrealResolvedObject texture, string outputDirectory)
    {
        if (texture.Package is null || texture.Export is null)
        {
            return null;
        }

        var textureName = NameHelpers.SanitizeMaterialName(texture.ObjectName);
        var outputPath = Path.Combine(outputDirectory, textureName + ".dds");
        using var stream = File.OpenRead(texture.Package.FilePath);
        using var binaryReader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var reader = new UnrealObjectReader(texture.Package, binaryReader, texture.Export);
        var properties = reader.ReadProperties();
        var format = properties.FirstInteger("Format") ?? properties.FirstInteger("CompFormat");
        if (format is null)
        {
            return null;
        }

        IReadOnlyList<UnrealTextureMip> mips;
        try
        {
            mips = reader.ReadArray(() => ReadMip(reader))
                .Where(mip => mip.Data.Count > 0 && mip.Width > 0 && mip.Height > 0)
                .ToArray();
        }
        catch (GMConverterException)
        {
            return null;
        }

        if (mips.Count == 0)
        {
            return null;
        }

        var fourCc = GetDdsFourCc(format.Value);
        if (fourCc is null)
        {
            return null;
        }

        if (!File.Exists(outputPath))
        {
            Directory.CreateDirectory(outputDirectory);
            using var outputStream = File.Create(outputPath);
            using var writer = new BinaryWriter(outputStream, Encoding.UTF8, leaveOpen: false);
            WriteDds(writer, mips, fourCc);
        }

        return new UnrealExportedTexture(textureName, TextureFormatHasAlpha(format.Value));
    }

    private static UnrealTextureMip ReadMip(UnrealObjectReader reader)
    {
        var data = reader.ReadLazyArray(reader.ReadByte);
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        return new UnrealTextureMip(data, width, height);
    }

    private static string? GetDdsFourCc(int textureFormat)
    {
        return textureFormat switch
        {
            _texfDxt1 => "DXT1",
            _texfDxt3 => "DXT3",
            _texfDxt5 => "DXT5",
            _ => null
        };
    }

    private static bool TextureFormatHasAlpha(int textureFormat)
    {
        return textureFormat is _texfDxt3 or _texfDxt5;
    }

    private static void WriteDds(BinaryWriter writer, IReadOnlyList<UnrealTextureMip> mips, string fourCc)
    {
        var firstMip = mips[0];
        var linearSize = CalculateDxtMipSize(firstMip.Width, firstMip.Height, fourCc);

        writer.Write(Encoding.ASCII.GetBytes("DDS "));
        writer.Write(124);
        writer.Write(0x00081007 | (mips.Count > 1 ? 0x00020000 : 0));
        writer.Write(firstMip.Height);
        writer.Write(firstMip.Width);
        writer.Write(linearSize);
        writer.Write(0);
        writer.Write(mips.Count);
        for (var i = 0; i < 11; i++)
        {
            writer.Write(0);
        }

        writer.Write(32);
        writer.Write(0x00000004);
        writer.Write(Encoding.ASCII.GetBytes(fourCc));
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        writer.Write(0x00001000 | (mips.Count > 1 ? 0x00400008 : 0));
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        foreach (var mip in mips)
        {
            writer.Write(mip.Data.ToArray());
        }
    }

    private static int CalculateDxtMipSize(int width, int height, string fourCc)
    {
        var blockSize = fourCc.Equals("DXT1", StringComparison.OrdinalIgnoreCase) ? 8 : 16;
        return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockSize;
    }

    private sealed record UnrealTextureMip(IReadOnlyList<byte> Data, int Width, int Height);
}

internal sealed record UnrealExportedTexture(string Name, bool HasAlpha);
