using CUE4Parse.FileProvider;

namespace GMConverter.Formats.Unreal;

internal sealed record Cue4ParseProviderContext(
    DefaultFileProvider Provider,
    IUnrealGameProfile Profile,
    string ArchiveDirectory);
