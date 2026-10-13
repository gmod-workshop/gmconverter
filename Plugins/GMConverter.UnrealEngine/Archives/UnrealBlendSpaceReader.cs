using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine.Animation;
using GMConverter.UnrealEngine.Formats.PSA;

namespace GMConverter.UnrealEngine.Archives;

/// <summary>
/// Reads an Unreal blend space or aim offset: its axes, and the object path, name and position of
/// each sample sequence. Exporting the samples is left to the caller.
/// </summary>
internal static class UnrealBlendSpaceReader
{
    // FBlendParameter's defaults; cooked assets leave out properties that are at their default,
    // and CUE4Parse would read a missing Max as 0.
    private const float _defaultMin = 0f;
    private const float _defaultMax = 100f;

    public static UnrealBlendSpace Read(UBlendSpaceBase blendSpace)
    {
        var axisCount = blendSpace is UBlendSpace1D ? 1 : 2;
        var axes = Enumerable.Range(0, axisCount).Select(index => ReadAxis(blendSpace, index)).ToArray();
        List<UnrealBlendSpaceSample> samples = [];
        foreach (var sample in blendSpace.SampleData)
        {
            if (sample.Animation?.ResolvedObject is not { } animation)
            {
                continue;
            }

            float[] coordinates = axisCount == 1 ? [sample.SampleValue.X] : [sample.SampleValue.X, sample.SampleValue.Y];
            samples.Add(new UnrealBlendSpaceSample(animation.GetPathName(), animation.Name.Text, coordinates));
        }

        return new UnrealBlendSpace(
            blendSpace.Name,
            blendSpace is UAimOffsetBlendSpace or UAimOffsetBlendSpace1D,
            axes,
            samples);
    }

    private static PSABlendSpaceAxis ReadAxis(UObject blendSpace, int index)
    {
        var parameter = blendSpace.Properties
            .FirstOrDefault(property => property.Name.Text == "BlendParameters" && property.ArrayIndex == index)?
            .Tag?.GenericValue is FScriptStruct { StructType: FStructFallback fallback }
                ? fallback
                : null;
        if (parameter is null)
        {
            return new PSABlendSpaceAxis(string.Empty, _defaultMin, _defaultMax);
        }

        bool Has(string name) => parameter.Properties.Any(property => property.Name.Text == name);
        return new PSABlendSpaceAxis(
            parameter.GetOrDefault("DisplayName", string.Empty),
            Has("Min") ? parameter.GetOrDefault<float>("Min") : _defaultMin,
            Has("Max") ? parameter.GetOrDefault<float>("Max") : _defaultMax);
    }
}
