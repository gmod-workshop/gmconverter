using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Wwise;
using GMConverter.UnrealEngine.Profiles;

namespace GMConverter.UnrealEngine.Archives;

internal sealed record Cue4ParseProviderContext(
    DefaultFileProvider Provider,
    IUnrealProfile Profile,
    string ArchiveDirectory)
{
    // Building it reads every soundbank in the game, so it waits for the first sound and is then
    // shared by every sound the provider resolves.
    private readonly Lazy<WwiseProvider> _wwise = new(() => new WwiseProvider(Provider, ArchiveDirectory));

    public WwiseProvider Wwise => _wwise.Value;
}
