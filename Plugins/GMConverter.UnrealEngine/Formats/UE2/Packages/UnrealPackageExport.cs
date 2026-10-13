namespace GMConverter.UnrealEngine.Formats.UE2.Packages;

internal sealed record UnrealPackageExport(
    int ClassIndex,
    int SuperIndex,
    int PackageIndex,
    string ObjectName,
    uint ObjectFlags,
    int SerialSize,
    int SerialOffset);
