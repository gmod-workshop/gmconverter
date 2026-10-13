using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Animation;

/// <summary>
/// Turns a model's blend spaces into Source blended sequences driven by pose parameters, such as
/// an aim offset into a <c>delta autoplay</c> layer blended by <c>aim_yaw</c> and <c>aim_pitch</c>.
/// Only blend spaces whose samples fill a regular grid and whose every axis maps to a pose
/// parameter are written; Source models have few pose parameters, so axes game code never sets
/// would only use them up.
/// </summary>
internal static class SourceBlendSequences
{
    private const float _coordinateTolerance = 0.001f;

    public static IReadOnlyList<SourceBlendSequence> Plan(
        Model model,
        IReadOnlyList<(AnimationClip Clip, string SmdPath)> animationSmdPaths,
        Func<BlendAxis, SourcePoseParameter?> getPoseParameter)
    {
        var smdByClip = animationSmdPaths.ToDictionary(entry => entry.Clip.Name, StringComparer.OrdinalIgnoreCase);
        List<SourceBlendSequence> plans = [];
        foreach (var blendSpace in model.BlendSpaces ?? [])
        {
            if (TryPlan(blendSpace, smdByClip, getPoseParameter, out var plan))
            {
                plans.Add(plan);
            }
            else
            {
                PerfTimer.Log("mdl.export", $"blend space {blendSpace.Name} skipped: no pose parameter for an axis, or samples don't fill a grid");
            }
        }

        return plans;
    }

    /// <summary>Writes the pose parameters the plans use, each once, with ranges covering every plan.</summary>
    public static void WritePoseParameters(StreamWriter writer, IReadOnlyList<SourceBlendSequence> plans)
    {
        var ranges = new Dictionary<string, (float Min, float Max)>(StringComparer.Ordinal);
        foreach (var (parameter, _, first, last) in plans.SelectMany(plan => plan.Axes))
        {
            var (min, max) = (MathF.Min(first, last), MathF.Max(first, last));
            ranges[parameter.Name] = ranges.TryGetValue(parameter.Name, out var range)
                ? (MathF.Min(range.Min, min), MathF.Max(range.Max, max))
                : (min, max);
        }

        foreach (var (name, (min, max)) in ranges)
        {
            writer.WriteLine(FormattableString.Invariant($"$poseparameter \"{name}\" {min:0.###} {max:0.###}"));
        }
    }

    /// <summary>
    /// Writes one plan: an <c>$animation</c> per sample (subtracting the base pose for layers),
    /// then the blended <c>$sequence</c>.
    /// </summary>
    public static void Write(StreamWriter writer, SourceBlendSequence plan, Func<AnimationPose?, string> getBaseAnimationName)
    {
        List<string> animationNames = [];
        for (var i = 0; i < plan.Clips.Count; i++)
        {
            var (clip, smdPath) = plan.Clips[i];
            var animationName = FormattableString.Invariant($"{plan.Name}_blend_{i}");
            var subtract = plan.Layering is { } layering
                ? FormattableString.Invariant($" subtract \"{getBaseAnimationName(layering.Base)}\" 0")
                : string.Empty;
            writer.WriteLine(FormattableString.Invariant(
                $"$animation \"{animationName}\" \"{Path.GetFileName(smdPath)}\" fps {clip.FrameRate:0.######}{subtract}"));
            animationNames.Add(animationName);
        }

        writer.WriteLine(FormattableString.Invariant($"$sequence \"{plan.Name}\" {{"));
        foreach (var animationName in animationNames)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"{animationName}\""));
        }

        writer.WriteLine(FormattableString.Invariant($"    blendwidth {plan.Axes[0].Width}"));
        foreach (var (parameter, _, first, last) in plan.Axes)
        {
            writer.WriteLine(FormattableString.Invariant($"    blend \"{parameter.Name}\" {first:0.###} {last:0.###}"));
        }

        if (plan.Layering is not null)
        {
            // An aim offset or similar layer plays on top of whatever the model is doing.
            writer.WriteLine("    delta");
            writer.WriteLine("    autoplay");
        }
        else if (plan.Clips.All(entry => entry.Clip.Loops))
        {
            writer.WriteLine("    loop");
        }

        writer.WriteLine("}");
    }

    private static bool TryPlan(
        AnimationBlendSpace blendSpace,
        Dictionary<string, (AnimationClip Clip, string SmdPath)> smdByClip,
        Func<BlendAxis, SourcePoseParameter?> getPoseParameter,
        out SourceBlendSequence plan)
    {
        plan = null!;
        if (blendSpace.Parameters.Count is < 1 or > 2 || blendSpace.Samples.Count == 0)
        {
            return false;
        }

        var parameters = blendSpace.Parameters.Select(parameter => getPoseParameter(parameter.Axis)).ToArray();
        if (parameters.Any(parameter => parameter is null))
        {
            return false;
        }

        // Each axis's distinct sample values are its grid lines; every pair of lines needs a sample.
        var lines = Enumerable.Range(0, parameters.Length)
            .Select(axis => DistinctSorted(blendSpace.Samples.Select(sample => sample.Coordinates[axis])))
            .ToArray();
        var cellCount = lines.Aggregate(1, (count, axisLines) => count * axisLines.Count);
        if (cellCount != blendSpace.Samples.Count || lines.Any(axisLines => axisLines.Count < 2))
        {
            return false;
        }

        List<(AnimationClip Clip, string SmdPath)> clips = [];
        var rows = parameters.Length == 2 ? lines[1] : [0f];
        foreach (var row in rows)
        {
            foreach (var column in lines[0])
            {
                var sample = blendSpace.Samples.FirstOrDefault(candidate =>
                    Near(candidate.Coordinates[0], column) && (parameters.Length == 1 || Near(candidate.Coordinates[1], row)));
                if (sample is null || !smdByClip.TryGetValue(sample.ClipName, out var entry))
                {
                    return false;
                }

                clips.Add(entry);
            }
        }

        // studiomdl blends from the first grid line to the last; a reversed parameter runs from the
        // negated first value to the negated last one, as in HL2's aim layers (aim_yaw 45 -45).
        var axes = parameters.Select((parameter, axis) =>
        {
            float ToSource(float value) => parameter!.Reversed ? -value : value;
            return (parameter!, lines[axis].Count, ToSource(lines[axis][0]), ToSource(lines[axis][^1]));
        }).ToArray();

        var layering = clips[0].Clip.Layering;
        plan = new SourceBlendSequence(blendSpace.Name, clips, axes, layering);
        return true;
    }

    private static List<float> DistinctSorted(IEnumerable<float> values)
    {
        // Where runs lazily, so each value is compared with the last line added so far.
        List<float> lines = [];
        foreach (var value in values.Order().Where(value => lines.Count == 0 || !Near(lines[^1], value)))
        {
            lines.Add(value);
        }

        return lines;
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) <= _coordinateTolerance;
}
