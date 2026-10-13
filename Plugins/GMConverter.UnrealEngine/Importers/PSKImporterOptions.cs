using GMConverter.SDK.Explorer;
using GMConverter.SDK.Options;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>
/// Option keys the PSK importer declares, shared with the Unreal explorers so a resolved entry can
/// hand its extracted animation straight to the importer.
/// </summary>
internal static class PSKImporterOptions
{
    public const string AnimationPath = "animationPath";
    public const string BlendSpacePath = "blendSpacePath";
    public const string VariantPath = "variantPath";

    /// <summary>Importer option values carrying <paramref name="animationPath"/>, or null without one.</summary>
    public static IReadOnlyDictionary<string, object?>? WithAnimation(string? animationPath)
    {
        return string.IsNullOrEmpty(animationPath)
            ? null
            : new Dictionary<string, object?> { [AnimationPath] = animationPath };
    }

    /// <summary>Importer option values carrying a blend space's sidecar and the PSAs of its samples.</summary>
    public static IReadOnlyDictionary<string, object?> WithBlendSpace(IEnumerable<string> samplePaths, string sidecarPath)
    {
        return new Dictionary<string, object?>
        {
            [AnimationPath] = string.Join(OptionDescriptor.MultipleValueSeparator, samplePaths.Distinct(StringComparer.OrdinalIgnoreCase)),
            [BlendSpacePath] = sidecarPath,
        };
    }

    /// <summary>Importer option values that add the mesh at <paramref name="inputPath"/> to another model as a variant.</summary>
    public static IReadOnlyDictionary<string, object?> AsVariant(string inputPath)
    {
        return new Dictionary<string, object?> { [VariantPath] = inputPath };
    }

    /// <summary>The animation path a resolved entry carries for the importer, if any.</summary>
    public static string? GetAnimationPath(ExplorerResolvedEntry entry)
    {
        return entry.ImporterOptions?.TryGetValue(AnimationPath, out var value) == true ? value as string : null;
    }
}
