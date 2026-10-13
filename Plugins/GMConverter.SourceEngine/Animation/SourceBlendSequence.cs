using GMConverter.SDK.Animation;

namespace GMConverter.SourceEngine.Animation;

/// <summary>
/// A blend space laid out the way studiomdl wants it: samples in grid order (the first parameter
/// changing fastest), with each axis's pose parameter and its range in Source's direction.
/// </summary>
/// <param name="Name">The sequence name.</param>
/// <param name="Clips">The sample clips with their animation SMDs, in grid order.</param>
/// <param name="Axes">Each axis's pose parameter, sample count, and the Source values of its first and last grid line.</param>
/// <param name="Layering">The clips' layering when the blend space is a layer, otherwise null.</param>
internal sealed record SourceBlendSequence(
    string Name,
    IReadOnlyList<(AnimationClip Clip, string SmdPath)> Clips,
    IReadOnlyList<(SourcePoseParameter Parameter, int Width, float First, float Last)> Axes,
    AnimationLayering? Layering);
