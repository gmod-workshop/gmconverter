using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.UnrealEngine.Formats.PSA;

namespace GMConverter.UnrealEngine.Importers;

/// <summary>
/// Turns blend space sidecars into the model's <see cref="Model.BlendSpaces"/>. A blend space is
/// kept only when every sample's clip was imported, since exporters can't blend a missing clip.
/// </summary>
internal static class PSKBlendSpaces
{
    public static Model Attach(Model model, ModelImportContext options)
    {
        var paths = options.Options.GetStrings(PSKImporterOptions.BlendSpacePath);
        if (paths.Count == 0)
        {
            return model;
        }

        var clipNames = new HashSet<string>((model.Animations ?? []).Select(clip => clip.Name), StringComparer.OrdinalIgnoreCase);
        List<AnimationBlendSpace> blendSpaces = [];
        foreach (var option in paths)
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(option));
            if (!File.Exists(path))
            {
                throw new GMConverterException($"Blend space file not found: {path}");
            }

            var sidecar = PSABlendSpaceSidecar.Read(path);
            var missing = sidecar.Samples.Where(sample => !clipNames.Contains(sample.ClipName)).Select(sample => sample.ClipName).ToArray();
            if (missing.Length > 0)
            {
                PerfTimer.Log("psk.import", $"blend space {sidecar.Name} skipped; missing clips: {string.Join(", ", missing)}");
                continue;
            }

            blendSpaces.Add(ToBlendSpace(sidecar, options));
        }

        return blendSpaces.Count == 0 ? model : model with { BlendSpaces = [.. model.BlendSpaces ?? [], .. blendSpaces] };
    }

    private static AnimationBlendSpace ToBlendSpace(PSABlendSpaceSidecar sidecar, ModelImportContext options)
    {
        var axes = sidecar.Axes.Select(axis => GuessAxis(axis.Name, sidecar.IsAimOffset)).ToArray();
        // Unreal speeds are in centimetres per second, like its positions, so they take the same scale.
        float Scale(int index, float value) => axes[index] is BlendAxis.MoveSpeed ? value * options.ScaleFactor * 0.01f : value;

        return new AnimationBlendSpace(
            sidecar.Name,
            [.. sidecar.Axes.Select((axis, index) => new BlendParameter(axis.Name, axes[index], Scale(index, axis.Min), Scale(index, axis.Max)))],
            [.. sidecar.Samples.Select(sample => new BlendSample(
                sample.ClipName,
                [.. sample.Coordinates.Take(axes.Length).Select((value, index) => Scale(index, value))]))]);
    }

    // Unreal's yaw and direction are positive to the character's right and its pitch is positive
    // upwards, which is the SDK's convention, so values pass through unchanged.
    private static BlendAxis GuessAxis(string name, bool isAimOffset)
    {
        var words = NameHelpers.SplitWords(name);
        return words.Contains("yaw") && (isAimOffset || words.Contains("aim")) ? BlendAxis.AimYaw
            : words.Contains("pitch") && (isAimOffset || words.Contains("aim")) ? BlendAxis.AimPitch
            : words.Contains("direction") ? BlendAxis.MoveDirection
            : words.Contains("speed") ? BlendAxis.MoveSpeed
            : BlendAxis.Generic;
    }
}
