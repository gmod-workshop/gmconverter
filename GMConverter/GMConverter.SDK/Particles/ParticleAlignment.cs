namespace GMConverter.SDK.Particles;

/// <summary>Which way a sprite particle faces.</summary>
public enum ParticleAlignment
{
    /// <summary>Square to the camera.</summary>
    Camera,

    /// <summary>Stretched along its velocity, facing the camera around it.</summary>
    Velocity,

    /// <summary>Square to the camera and as tall as it is wide: its size's X (after scaling over life) sets both.</summary>
    CameraSquare,
}
