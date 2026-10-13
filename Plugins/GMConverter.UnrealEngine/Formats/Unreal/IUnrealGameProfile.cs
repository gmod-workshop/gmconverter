using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using GMConverter.SDK.Games;

namespace GMConverter.UnrealEngine.Formats.Unreal;

/// <summary>
/// A game's Unreal-specific settings: how to open and mount its archives and which asset classes
/// the explorer lists. Identity and detection come from <see cref="IGameProfile"/>, so the host
/// can list and detect these games; the rest stays inside the plugin, looked up by id through
/// <see cref="UnrealGameProfiles"/>.
/// </summary>
internal interface IUnrealGameProfile : IGameProfile
{
    VersionContainer CreateVersionContainer();

    Cue4ParseGameData? TryGetGameData(string archiveDirectory);

    void ConfigureProvider(DefaultFileProvider provider, Cue4ParseGameData? gameData);

    void Mount(DefaultFileProvider provider, Cue4ParseGameData? gameData);

    bool TryGetExplorerAssetClass(string registryClass, out string assetClass);

    string? GetUnavailableEncryptedArchiveMessage(DefaultFileProvider provider);
}
