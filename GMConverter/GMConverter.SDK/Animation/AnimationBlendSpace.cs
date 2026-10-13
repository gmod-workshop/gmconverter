namespace GMConverter.SDK.Animation;

/// <summary>
/// A set of clips blended by one or two parameters, such as aim poses blended by aim yaw and
/// pitch, or walk cycles blended by direction. The samples refer to clips in the model's
/// <see cref="Geometry.Model.Animations"/>; when those clips are layers, the blend space is a
/// layer too.
/// </summary>
/// <param name="Name">The blend space's name.</param>
/// <param name="Parameters">Its axes, one or two.</param>
/// <param name="Samples">The clips and where they sit on the axes.</param>
public sealed record AnimationBlendSpace(string Name, IReadOnlyList<BlendParameter> Parameters, IReadOnlyList<BlendSample> Samples);
