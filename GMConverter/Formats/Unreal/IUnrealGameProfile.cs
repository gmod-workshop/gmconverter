using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

namespace GMConverter.Formats.Unreal;

internal interface IUnrealGameProfile
{
    string Id { get; }

    string DisplayName { get; }

    bool Supports(string archiveDirectory);

    VersionContainer CreateVersionContainer();

    Cue4ParseGameData? TryGetGameData();

    void ConfigureProvider(DefaultFileProvider provider, Cue4ParseGameData? gameData);

    void Mount(DefaultFileProvider provider, Cue4ParseGameData? gameData);

    bool TryGetExplorerAssetClass(string registryClass, out string assetClass);

    string? GetUnavailableEncryptedArchiveMessage(DefaultFileProvider provider);
}
