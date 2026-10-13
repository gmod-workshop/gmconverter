namespace GMConverter.SDK.Animation;

/// <summary>One axis of a blend space.</summary>
/// <param name="Name">The axis name as authored, e.g. <c>"Yaw"</c>.</param>
/// <param name="Axis">What the axis controls, or <see cref="BlendAxis.Generic"/> when unknown.</param>
/// <param name="Min">The lowest value on the axis.</param>
/// <param name="Max">The highest value on the axis.</param>
public sealed record BlendParameter(string Name, BlendAxis Axis, float Min, float Max);
