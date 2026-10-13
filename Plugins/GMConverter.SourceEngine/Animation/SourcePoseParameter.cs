namespace GMConverter.SourceEngine.Animation;

/// <summary>
/// The Source pose parameter a blend axis drives.
/// </summary>
/// <param name="Name">The parameter name game code sets, e.g. <c>aim_yaw</c>.</param>
/// <param name="Reversed">
/// True when Source counts the other way from the SDK's axis, such as Source's yaw growing to the
/// left; values are negated.
/// </param>
internal sealed record SourcePoseParameter(string Name, bool Reversed);
