namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>How a UE3 package body or bulk data block is compressed.</summary>
internal enum UE3CompressionMethod
{
    None,
    Zlib,
    Lzo,
    Lzx,

    /// <summary>LZ4 blocks, which stock UE3 never used; CrossFire HD compresses its packages and textures with it.</summary>
    Lz4
}
