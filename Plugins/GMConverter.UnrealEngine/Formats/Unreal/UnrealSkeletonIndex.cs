using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;

namespace GMConverter.UnrealEngine.Formats.Unreal;

// Finds the skeleton a skeletal mesh or animation package is authored against. Cooked asset
// registries strip both the "Skeleton" tag and the dependency graph, but every IoStore container
// header still lists each package's imported packages, and a mesh or animation always imports its
// skeleton's package. Reading those headers costs ~100 ms for a whole game, so pairing needs no
// package loads.
internal sealed class UnrealSkeletonIndex
{
    private readonly IReadOnlyDictionary<FPackageId, FPackageId[]> _importsByPackage;
    private readonly Dictionary<FPackageId, string> _skeletonPackages;

    public UnrealSkeletonIndex(
        IReadOnlyDictionary<FPackageId, FPackageId[]> importsByPackage,
        IEnumerable<string> skeletonPackageNames)
    {
        _importsByPackage = importsByPackage;
        _skeletonPackages = [];
        foreach (var name in skeletonPackageNames)
        {
            _skeletonPackages.TryAdd(FPackageId.FromName(name), name);
        }
    }

    public static UnrealSkeletonIndex FromContainers(
        IEnumerable<IoStoreReader> containers,
        IEnumerable<string> skeletonPackageNames)
    {
        Dictionary<FPackageId, FPackageId[]> importsByPackage = [];
        foreach (var container in containers)
        {
            if (container.ContainerHeader is not { StoreEntries.Length: > 0 } header)
            {
                continue;
            }

            for (var index = 0; index < header.StoreEntries.Length; index++)
            {
                importsByPackage.TryAdd(header.PackageIds[index], header.StoreEntries[index].ImportedPackages ?? []);
            }
        }

        return new UnrealSkeletonIndex(importsByPackage, skeletonPackageNames);
    }

    /// <summary>The skeleton package <paramref name="packageName"/> imports, or null if none is known.</summary>
    public string? FindSkeleton(string packageName)
    {
        var packageId = FPackageId.FromName(packageName);
        if (_skeletonPackages.TryGetValue(packageId, out var self))
        {
            return self;
        }

        if (!_importsByPackage.TryGetValue(packageId, out var imports))
        {
            return null;
        }

        foreach (var import in imports)
        {
            if (_skeletonPackages.TryGetValue(import, out var skeleton))
            {
                return skeleton;
            }
        }

        return null;
    }
}
