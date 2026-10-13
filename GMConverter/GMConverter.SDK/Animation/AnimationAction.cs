namespace GMConverter.SDK.Animation;

/// <summary>
/// What a character is doing in an animation, as far as game code choosing animations cares. Only
/// steady actions are listed; transitions such as starting or stopping a run have no action.
/// </summary>
public enum AnimationAction
{
    /// <summary>Standing still, relaxed.</summary>
    Idle,

    /// <summary>Standing still, ready to fight (weapon raised).</summary>
    CombatIdle,

    Walk,

    /// <summary>A pace between walking and running.</summary>
    Jog,

    Run,

    CrouchIdle,

    CrouchWalk,

    /// <summary>Turning on the spot; the direction says which way.</summary>
    Turn,

    Jump,

    /// <summary>Falling or airborne after a jump.</summary>
    Fall,

    Land,

    /// <summary>Firing or using the held weapon.</summary>
    Attack,

    MeleeAttack,

    Reload,

    /// <summary>A small reaction to being hit; the direction says where the hit came from.</summary>
    Flinch,

    /// <summary>A large reaction to being hit that throws the character off balance.</summary>
    Stagger,

    Death,
}
