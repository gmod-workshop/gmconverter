using CUE4Parse.UE4.Objects.Core.Misc;

namespace GMConverter.UnrealEngine.Formats.Unreal;

internal sealed record Cue4ParseGameData(
    string Version,
    string? MappingsPath,
    IReadOnlyDictionary<FGuid, string> AesKeys);
