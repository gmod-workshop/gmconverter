using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;

namespace GMConverter.UnrealEngine.Animation;

/// <summary>
/// Guesses what each animation is for from its name, following the naming most Unreal projects
/// use: <c>[A_][Character_]Stance_Action[_Direction][_Variant]</c>, such as
/// <c>A_B1_2HRifle_Run_F</c> or <c>A_B1_2HRifle_HitReact_L_Med</c>. Only steady actions get a
/// role: starts, stops, turns past 90°, enter/exit transitions, additive hit layers and aim
/// poses are left without one, since game code doesn't pick them by action.
/// </summary>
internal static class AnimationRoleGuesser
{
    private static readonly HashSet<string> _transitionWords = new(StringComparer.Ordinal)
    {
        "start", "stop", "enter", "exit", "pose", "ref", "break", "displace", "climb", "step", "corner", "throw", "add", "additive", "pivot", "transition",
    };

    // The cells of an aim offset grid (left/centre/right by up/centre/down), e.g. Aim_LU.
    private static readonly HashSet<string> _aimGridWords = new(StringComparer.Ordinal) { "lu", "lc", "ld", "cu", "cc", "cd", "ru", "rc", "rd" };

    /// <summary>Sets <see cref="AnimationClip.Role"/> on every clip that has none and whose name reveals one.</summary>
    public static Model Apply(Model model)
    {
        if (model.Animations is not { Count: > 0 } clips || clips.All(clip => clip.Role is not null))
        {
            return model;
        }

        return model with { Animations = [.. clips.Select(clip => clip.Role is null && Guess(clip.Name) is { } role ? clip with { Role = role } : clip)] };
    }

    public static AnimationRole? Guess(string clipName)
    {
        var words = NameHelpers.SplitWords(clipName);
        if (words.Any(_transitionWords.Contains) || words.Contains("aim") && words.Any(_aimGridWords.Contains))
        {
            return null;
        }

        return GuessAction(words) is { } action ? new AnimationRole(action, GuessStance(words), GuessDirection(words)) : null;
    }

    private static AnimationAction? GuessAction(List<string> words)
    {
        bool Has(params string[] candidates) => candidates.Any(words.Contains);

        if (Has("death", "die", "dying"))
        {
            return AnimationAction.Death;
        }

        if (Has("stagger"))
        {
            return AnimationAction.Stagger;
        }

        if (Has("flinch", "hitreact", "hit") || Has("react"))
        {
            // A flinch's idle is the loop held while flinching, not a reaction to play.
            return Has("idle") ? null : AnimationAction.Flinch;
        }

        if (Has("reload"))
        {
            return AnimationAction.Reload;
        }

        if (Has("melee", "swing", "slash", "punch", "bash"))
        {
            return AnimationAction.MeleeAttack;
        }

        if (Has("fire", "shoot", "shot", "attack"))
        {
            return AnimationAction.Attack;
        }

        if (Has("jump"))
        {
            return AnimationAction.Jump;
        }

        if (Has("fall", "falling", "inair"))
        {
            return AnimationAction.Fall;
        }

        if (Has("land", "landing"))
        {
            return AnimationAction.Land;
        }

        if (Has("turn"))
        {
            // Source's turn activities are 90° turns, and crouched turns have no activity.
            return Has("180") || Has("crouch") ? null : AnimationAction.Turn;
        }

        if (Has("crouch", "crouched"))
        {
            return Has("idle") ? AnimationAction.CrouchIdle
                : Has("walk", "jog", "run") ? AnimationAction.CrouchWalk
                : null;
        }

        if (Has("idle"))
        {
            return Has("aim", "combat", "alert") ? AnimationAction.CombatIdle : AnimationAction.Idle;
        }

        if (Has("run", "sprint"))
        {
            return AnimationAction.Run;
        }

        if (Has("jog"))
        {
            return AnimationAction.Jog;
        }

        return Has("walk") ? AnimationAction.Walk : null;
    }

    private static WeaponStance? GuessStance(List<string> words)
    {
        bool Contains(string part) => words.Any(word => word.Contains(part, StringComparison.Ordinal));

        return Contains("rifle") || Contains("smg") ? WeaponStance.Rifle
            : Contains("pistol") || Contains("handgun") ? WeaponStance.Pistol
            : Contains("shotgun") ? WeaponStance.Shotgun
            : Contains("rpg") || Contains("launcher") || Contains("heavy") ? WeaponStance.Heavy
            : Contains("unarmed") || Contains("noweapon") ? WeaponStance.Unarmed
            : Contains("sword") || Contains("knife") || words.Contains("melee") ? WeaponStance.Melee
            : null;
    }

    private static MoveDirection? GuessDirection(List<string> words)
    {
        bool Has(params string[] candidates) => candidates.Any(words.Contains);

        return Has("f", "fwd", "forward", "front") ? MoveDirection.Forward
            : Has("b", "bwd", "back", "backward", "backwards") ? MoveDirection.Backward
            : Has("l", "left") ? MoveDirection.Left
            : Has("r", "right") ? MoveDirection.Right
            : null;
    }
}
