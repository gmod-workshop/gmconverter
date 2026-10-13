using GMConverter.SDK.Animation;

namespace GMConverter.SourceEngine.Games;

// Source game code plays animations by activity, not by name. These map neutral animation roles
// to the activity names NPC code (shared by Source games) and Garry's Mod player code look for.
// studiomdl accepts any ACT_ name, so a role without a sensible activity simply gets none.
internal static class SourceActivities
{
    /// <summary>The NPC activity for <paramref name="role"/>, or null when NPC code has none for it.</summary>
    public static string? Npc(AnimationRole role)
    {
        if (IsMovement(role.Action) && role.Direction is not (null or MoveDirection.Forward))
        {
            // Strafing and backpedalling come from blend spaces, not single activities.
            return null;
        }

        return role.Action switch
        {
            AnimationAction.Idle => "ACT_IDLE",
            AnimationAction.CombatIdle => "ACT_IDLE_ANGRY",
            AnimationAction.Walk => "ACT_WALK",
            AnimationAction.Run => "ACT_RUN",
            AnimationAction.CrouchIdle => "ACT_CROUCHIDLE",
            AnimationAction.CrouchWalk => "ACT_WALK_CROUCH",
            AnimationAction.Turn => role.Direction switch
            {
                MoveDirection.Left => "ACT_TURN_LEFT",
                MoveDirection.Right => "ACT_TURN_RIGHT",
                _ => null,
            },
            AnimationAction.Jump => "ACT_JUMP",
            AnimationAction.Fall => "ACT_GLIDE",
            AnimationAction.Land => "ACT_LAND",
            AnimationAction.Attack => "ACT_RANGE_ATTACK1",
            AnimationAction.MeleeAttack => "ACT_MELEE_ATTACK1",
            AnimationAction.Reload => "ACT_RELOAD",
            AnimationAction.Flinch => "ACT_SMALL_FLINCH",
            AnimationAction.Stagger => "ACT_BIG_FLINCH",
            AnimationAction.Death => "ACT_DIESIMPLE",
            // A jog sits between ACT_WALK and ACT_RUN; NPC code has no activity for it.
            _ => null,
        };
    }

    /// <summary>
    /// The Garry's Mod player activity for <paramref name="role"/> (the <c>ACT_HL2MP_*</c> set,
    /// suffixed with the weapon's hold type), or null when player code has none for it.
    /// </summary>
    public static string? Player(AnimationRole role)
    {
        if (IsMovement(role.Action) && role.Direction is not (null or MoveDirection.Forward))
        {
            return null;
        }

        var holdType = role.Stance switch
        {
            WeaponStance.Pistol => "_PISTOL",
            WeaponStance.Rifle => "_AR2",
            WeaponStance.Shotgun => "_SHOTGUN",
            WeaponStance.Heavy => "_RPG",
            WeaponStance.Melee => "_MELEE",
            _ => string.Empty,
        };

        return role.Action switch
        {
            AnimationAction.Idle or AnimationAction.CombatIdle => "ACT_HL2MP_IDLE" + holdType,
            AnimationAction.Walk => "ACT_HL2MP_WALK" + holdType,
            AnimationAction.Run => "ACT_HL2MP_RUN" + holdType,
            AnimationAction.CrouchIdle => "ACT_HL2MP_IDLE_CROUCH" + holdType,
            AnimationAction.CrouchWalk => "ACT_HL2MP_WALK_CROUCH" + holdType,
            AnimationAction.Jump => "ACT_HL2MP_JUMP" + holdType,
            AnimationAction.Attack or AnimationAction.MeleeAttack => "ACT_HL2MP_GESTURE_RANGE_ATTACK" + holdType,
            AnimationAction.Reload => "ACT_HL2MP_GESTURE_RELOAD" + holdType,
            AnimationAction.Death => "ACT_DIESIMPLE",
            _ => null,
        };
    }

    private static bool IsMovement(AnimationAction action) =>
        action is AnimationAction.Walk or AnimationAction.Jog or AnimationAction.Run or AnimationAction.CrouchWalk;
}
