namespace GMConverter.Unity.Animation;

/// <summary>The transform property a clip binding animates.</summary>
internal enum UnityTransformCurve
{
    None,
    Position,
    Rotation,
    Scale,

    // Euler angles in degrees, applied Z, then X, then Y.
    Euler,
}
