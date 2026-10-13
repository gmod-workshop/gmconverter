namespace GMConverter.UnrealEngine.Formats.UE2.Packages;

internal sealed record UnrealPackageSummary(
    int FileVersion,
    int LicenseeVersion,
    int PackageFlags,
    int NameCount,
    int NameOffset,
    int ExportCount,
    int ExportOffset,
    int ImportCount,
    int ImportOffset,
    Guid Guid,
    IReadOnlyList<UnrealPackageGeneration> Generations);
