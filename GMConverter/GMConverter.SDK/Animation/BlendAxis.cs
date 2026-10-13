namespace GMConverter.SDK.Animation;

/// <summary>
/// What a blend space parameter controls, so exporters can map it onto the target game's own
/// parameters. Angles are in degrees from the character's facing.
/// </summary>
public enum BlendAxis
{
    /// <summary>Something no exporter has a standard parameter for.</summary>
    Generic,

    /// <summary>Where the character aims sideways; positive is to its right.</summary>
    AimYaw,

    /// <summary>Where the character aims vertically; positive is up.</summary>
    AimPitch,

    /// <summary>The direction the character moves in; positive is to its right, ±180 is backwards.</summary>
    MoveDirection,

    /// <summary>How fast the character moves, in the model's units per second.</summary>
    MoveSpeed,
}
