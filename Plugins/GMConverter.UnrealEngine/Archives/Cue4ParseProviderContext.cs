using CUE4Parse.FileProvider;
using GMConverter.UnrealEngine.Profiles;

namespace GMConverter.UnrealEngine.Archives;

internal sealed record Cue4ParseProviderContext(
    DefaultFileProvider Provider,
    IUnrealProfile Profile,
    string ArchiveDirectory);
