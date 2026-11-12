using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using GMConverter.SDK.Explorer;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Profiles;
using GMConverter.UnrealEngine.Animation;
using GMConverter.UnrealEngine.Archives;

namespace GMConverter.UnrealEngine.Profiles;

/// <summary>
/// Any Unreal Engine 4/5 game without a profile of its own: UE 5.6 settings, a <c>.usmap</c>
/// found next to the archives or in the install folder, and no encryption keys. It claims every
/// Unreal archive folder as a <see cref="GameMatch.Fallback"/>, so a specific game always wins.
/// Games that only differ in identity derive from it.
/// </summary>
internal class GenericUnrealProfile : IUnrealProfile
{
    public virtual string Id => "unreal";

    public virtual string DisplayName => "Other Unreal Engine game";

    public virtual string Engine => "Unreal Engine 4/5";

    public virtual GameMatch Detect(ExplorerTarget target)
    {
        return Cue4ParseProviderFactory.IsArchiveTarget(target) ? GameMatch.Fallback : GameMatch.None;
    }

    /// <summary>Gives animations roles from their names; see <see cref="AnimationRoleGuesser"/>.</summary>
    public virtual Model Prepare(Model model) => AnimationRoleGuesser.Apply(model);

    public VersionContainer CreateVersionContainer()
    {
        return new VersionContainer(EGame.GAME_UE5_6, ETexturePlatform.DesktopMobile);
    }

    public Cue4ParseGameData? TryGetGameData(string archiveDirectory)
    {
        var mappingsPath = UsmapLocator.Find(archiveDirectory);
        return mappingsPath is null
            ? null
            : new Cue4ParseGameData(string.Empty, mappingsPath, new Dictionary<FGuid, string>());
    }

    public void ConfigureProvider(DefaultFileProvider provider, Cue4ParseGameData? gameData)
    {
        if (!string.IsNullOrWhiteSpace(gameData?.MappingsPath))
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(gameData.MappingsPath);
        }
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
            string.Equals(registryClass, "PoseAsset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(registryClass, "AkAudioEvent", StringComparison.OrdinalIgnoreCase);
    }

    public virtual UnrealMaterialConventions MaterialConventions => UnrealMaterialConventions.None;

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
