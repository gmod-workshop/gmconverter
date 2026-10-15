namespace GMConverter.Godot.Animation;

/// <summary>What a <see cref="GodotAnimationTrack"/> keys.</summary>
internal enum GodotAnimationTrackKind
{
    /// <summary>A node's or bone's position (<c>position_3d</c>).</summary>
    Position,

    /// <summary>A node's or bone's rotation (<c>rotation_3d</c>).</summary>
    Rotation,

    /// <summary>A node's or bone's scale (<c>scale_3d</c>).</summary>
    Scale,

    /// <summary>A node property (<c>value</c>), such as <c>position</c> or <c>visible</c>.</summary>
    Value,
}
