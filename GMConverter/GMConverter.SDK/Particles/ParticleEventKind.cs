namespace GMConverter.SDK.Particles;

/// <summary>Which moment in another emitter's particles' lives releases particles.</summary>
public enum ParticleEventKind
{
    /// <summary>When each of its particles is released.</summary>
    Spawn,

    /// <summary>When each of its particles dies.</summary>
    Death,
}
