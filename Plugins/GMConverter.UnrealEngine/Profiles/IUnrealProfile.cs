using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using GMConverter.SDK.Profiles;
using GMConverter.UnrealEngine.Archives;

namespace GMConverter.UnrealEngine.Profiles;

/// <summary>
/// A game's Unreal-specific settings: how to open and mount its archives and which asset classes
/// the explorer lists. Identity and detection come from <see cref="IImportGameProfile"/>, so the host
/// can list and detect these games; the rest stays inside the plugin, looked up by id through
/// <see cref="UnrealProfiles"/>.
/// </summary>
internal interface IUnrealProfile : IImportGameProfile
{
    VersionContainer CreateVersionContainer();

    Cue4ParseGameData? TryGetGameData(string archiveDirectory);

    void ConfigureProvider(DefaultFileProvider provider, Cue4ParseGameData? gameData);

    void Mount(DefaultFileProvider provider, Cue4ParseGameData? gameData);

    bool TryGetExplorerAssetClass(string registryClass, out string assetClass);

    /// <summary>
    /// The game's own material conventions: which material parameters hold its base colour,
    /// normal and mask textures, and how the mask is packed. Checked before the generic aliases.
    /// </summary>
    UnrealMaterialConventions MaterialConventions => UnrealMaterialConventions.None;

    string? GetUnavailableEncryptedArchiveMessage(DefaultFileProvider provider);
}
