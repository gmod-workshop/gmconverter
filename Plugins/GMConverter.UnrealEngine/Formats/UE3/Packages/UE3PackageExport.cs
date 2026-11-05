namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// An object a package defines. Indexes are package indexes (positive for exports, negative for
/// imports, zero for none); <c>ClassIndex</c> is zero when the object is itself a class, and
/// <c>OuterIndex</c> zero for a top-level object. <c>SerialOffset</c> is where the object's data
/// starts in the decompressed package.
/// </summary>
internal sealed record UE3PackageExport(
    int ClassIndex,
    int SuperIndex,
    int OuterIndex,
    string ObjectName,
    int ArchetypeIndex,
    ulong ObjectFlags,
    int SerialSize,
    int SerialOffset);
