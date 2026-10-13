using CUE4Parse.UE4.Objects.Core.Misc;

namespace GMConverter.UnrealEngine.Archives;

internal sealed record Cue4ParseGameData(
    string Version,
    string? MappingsPath,
    IReadOnlyDictionary<FGuid, string> AesKeys);
