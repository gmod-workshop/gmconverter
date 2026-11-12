namespace GMConverter.SDK.Animation;

/// <summary>What happens at an <see cref="AnimationEvent"/>.</summary>
public enum AnimationEventKind
{
    /// <summary>A foot touches the ground; <see cref="AnimationEvent.Bone"/> names the foot when known.</summary>
    Footstep,

    /// <summary>A sound plays; <see cref="AnimationEvent.Sound"/> names it.</summary>
    Sound,

    /// <summary>The held weapon fires.</summary>
    FireWeapon,

    /// <summary>
    /// A particle effect starts; <see cref="AnimationEvent.Effect"/> names it and
    /// <see cref="AnimationEvent.Bone"/> the bone or socket it starts at and follows.
    /// </summary>
    ParticleEffect,
}
