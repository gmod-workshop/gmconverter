using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Versions;

namespace GMConverter.Formats.Unreal;

internal sealed class GenericUnrealGameProfile : IUnrealGameProfile
{
    public string Id => "generic";

    public string DisplayName => "Unreal Engine 4/5";

    public bool Supports(string archiveDirectory)
    {
        return Directory.Exists(archiveDirectory);
    }

    public VersionContainer CreateVersionContainer()
    {
        return new VersionContainer(EGame.GAME_UE5_6, ETexturePlatform.DesktopMobile);
    }

    public Cue4ParseGameData? TryGetGameData()
    {
        return null;
    }

    public void ConfigureProvider(DefaultFileProvider provider, Cue4ParseGameData? gameData)
    {
    }

    public void Mount(DefaultFileProvider provider, Cue4ParseGameData? gameData)
    {
        provider.Mount();
        provider.LoadVirtualPaths();
        provider.PostMount();
    }

    public bool TryGetExplorerAssetClass(string registryClass, out string assetClass)
    {
        assetClass = registryClass;
        return string.Equals(registryClass, "SkeletalMesh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "StaticMesh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "AnimationAsset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "AnimBlueprint", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "AnimComposite", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "AnimMontage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "AnimSequence", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "AnimStreamable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "BlendSpace", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "BlendSpace1D", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "PoseAsset", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetUnavailableEncryptedArchiveMessage(DefaultFileProvider provider)
    {
        if (provider.UnloadedVfs.Count == 0)
        {
            return null;
        }

        var requiredKeys = provider.RequiredKeys
            .Select(guid => guid.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        return requiredKeys.Length == 0
            ? "UE4/UE5 archives are still locked after mounting. Additional encryption keys may be required."
            : $"UE4/UE5 archives are still locked after mounting. Missing key GUIDs include: {string.Join(", ", requiredKeys)}.";
    }
}
