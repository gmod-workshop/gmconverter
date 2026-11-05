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

    /// <summary>Taking out the held weapon, as a first-person view model does when it is selected.</summary>
    Draw,

    /// <summary>Putting the held weapon away.</summary>
    Holster,

    /// <summary>The held weapon's other attack, such as a knife's heavy stab or a gun's alternate fire.</summary>
    SecondaryAttack,

    /// <summary>Turning the held weapon over to admire it.</summary>
    Inspect,
}
