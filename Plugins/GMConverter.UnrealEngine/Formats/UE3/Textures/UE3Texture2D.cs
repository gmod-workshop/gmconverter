using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Textures;

/// <summary>
/// A UE3 Texture2D: its pixel format (the enum name, such as <c>PF_DXT1</c>), sampling and mips.
/// <c>CacheName</c> is the texture cache (TextureFileCacheName) holding mips stored outside the package.
/// </summary>
internal sealed record UE3Texture2D(
    string Name,
    string Format,
    int Width,
    int Height,
    UE3TextureAddress AddressX,
    UE3TextureAddress AddressY,
    bool Srgb,
    string? CacheName,
    IReadOnlyList<UE3TextureMip> Mips)
{
    public static UE3Texture2D Read(UE3Package package, UE3PackageExport export)
    {
        var (archive, properties) = package.OpenObject(export);
        var version = package.Summary.FileVersion;
        if (version < 567)
        {
            throw new GMConverterException($"UE3 texture {export.ObjectName}: package version {version} is older than this reader supports.");
        }

        UE3BulkData.Read(archive); // source art, which cooked games leave empty
        var mips = archive.ReadArray(mipArchive =>
        {
            var data = UE3BulkData.Read(mipArchive);
            var width = mipArchive.ReadInt32();
            var height = mipArchive.ReadInt32();
            return new UE3TextureMip(width, height, data);
        });

        return new UE3Texture2D(
            export.ObjectName,
            properties.GetName("Format") ?? "PF_Unknown",
            properties.GetInt("SizeX") ?? mips.FirstOrDefault()?.Width ?? 0,
            properties.GetInt("SizeY") ?? mips.FirstOrDefault()?.Height ?? 0,
            ParseAddress(properties.GetName("AddressX")),
            ParseAddress(properties.GetName("AddressY")),
            properties.GetBool("SRGB") ?? true,
            properties.GetName("TextureFileCacheName"),
            mips);
    }

    private static UE3TextureAddress ParseAddress(string? value)
    {
        return value switch
        {
            "TA_Clamp" => UE3TextureAddress.Clamp,
            "TA_Mirror" => UE3TextureAddress.Mirror,
            _ => UE3TextureAddress.Wrap,
        };
    }
}
