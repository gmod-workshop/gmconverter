namespace GMConverter.Godot.Animation;

/// <summary>How a Godot animation track moves between keys (<c>Animation.InterpolationType</c>).</summary>
internal enum GodotInterpolation
{
    /// <summary>Each key holds until the next.</summary>
    Nearest = 0,

    Linear = 1,

    /// <summary>A smooth curve through the keys.</summary>
    Cubic = 2,

    /// <summary>Linear, taking the shorter way round for angles.</summary>
    LinearAngle = 3,

    /// <summary>Cubic, taking the shorter way round for angles.</summary>
    CubicAngle = 4,
}
