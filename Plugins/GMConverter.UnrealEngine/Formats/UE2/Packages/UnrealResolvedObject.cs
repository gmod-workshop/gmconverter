namespace GMConverter.UnrealEngine.Formats.UE2.Packages;

internal sealed record UnrealResolvedObject(
    string ObjectName,
    string ClassName,
    UnrealPackageFile? Package,
    UnrealPackageExport? Export)
{
    public static UnrealResolvedObject Missing(string objectName, string className)
    {
        return new UnrealResolvedObject(objectName, className, null, null);
    }
}
