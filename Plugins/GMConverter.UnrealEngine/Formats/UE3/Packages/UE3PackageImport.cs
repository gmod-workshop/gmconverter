namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// An object a package uses from another package. <c>OuterIndex</c> is the import's outer as a
/// package index (negative for imports), or zero for a top-level package.
/// </summary>
internal sealed record UE3PackageImport(string ClassPackage, string ClassName, int OuterIndex, string ObjectName);
