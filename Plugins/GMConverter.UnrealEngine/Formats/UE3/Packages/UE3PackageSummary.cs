namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// The fixed header at the start of an Unreal Engine 3 package. <c>FileVersion</c> is the engine's
/// package version (872 for CrossFire HD) and <c>LicenseeVersion</c> the game's own. <c>HeaderSize</c>
/// covers the tables, which end where the dependency table (<c>DependsOffset</c>) starts, and
/// <c>Chunks</c> lists the compressed blocks the rest is split into.
/// </summary>
internal sealed record UE3PackageSummary(
    int FileVersion,
    int LicenseeVersion,
    int HeaderSize,
    uint PackageFlags,
    int NameCount,
    int NameOffset,
    int ExportCount,
    int ExportOffset,
    int ImportCount,
    int ImportOffset,
    int DependsOffset,
    int EngineVersion,
    uint CompressionFlags,
    IReadOnlyList<UE3CompressedChunk> Chunks);
