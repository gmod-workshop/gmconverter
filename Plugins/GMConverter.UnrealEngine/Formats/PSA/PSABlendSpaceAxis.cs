namespace GMConverter.UnrealEngine.Formats.PSA;

/// <summary>One axis of an Unreal blend space, as authored.</summary>
/// <param name="Name">The axis display name, e.g. <c>"Yaw"</c>.</param>
/// <param name="Min">The lowest value on the axis.</param>
/// <param name="Max">The highest value on the axis.</param>
internal sealed record PSABlendSpaceAxis(string Name, float Min, float Max);
