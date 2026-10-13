namespace GMConverter.UnrealEngine.Formats.UE2.Packages;

internal sealed record UnrealPackageImport(
    string ClassPackage,
    string ClassName,
    int PackageIndex,
    string ObjectName);
