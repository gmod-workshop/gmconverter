using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>An object in a particular package, as found by following a reference.</summary>
internal readonly record struct UE3ObjectReference(UE3Package Package, UE3PackageExport Export)
{
    public string ClassName => Package.GetClassName(Export);

    public string Key => $"{Package.FilePath}|{Export.SerialOffset}";
}
