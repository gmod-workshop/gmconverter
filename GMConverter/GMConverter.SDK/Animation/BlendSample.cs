namespace GMConverter.SDK.Animation;

/// <summary>A clip placed at a point in a blend space.</summary>
/// <param name="ClipName">The <see cref="AnimationClip.Name"/> of a clip in the same model.</param>
/// <param name="Coordinates">The clip's position, one value per <see cref="AnimationBlendSpace.Parameters"/> entry.</param>
public sealed record BlendSample(string ClipName, IReadOnlyList<float> Coordinates);
