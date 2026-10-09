using GMConverter.SDK.Explorer;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>
/// Option keys the PSK importer declares, shared with the Unreal explorers so a resolved entry can
/// hand its extracted animation straight to the importer.
/// </summary>
internal static class PSKImporterOptions
{
    public const string AnimationPath = "animationPath";

    /// <summary>Importer option values carrying <paramref name="animationPath"/>, or null without one.</summary>
    public static IReadOnlyDictionary<string, object?>? WithAnimation(string? animationPath)
    {
        return string.IsNullOrEmpty(animationPath)
            ? null
            : new Dictionary<string, object?> { [AnimationPath] = animationPath };
    }

    /// <summary>The animation path a resolved entry carries for the importer, if any.</summary>
    public static string? GetAnimationPath(ExplorerResolvedEntry entry)
    {
        return entry.ImporterOptions?.TryGetValue(AnimationPath, out var value) == true ? value as string : null;
    }
}
