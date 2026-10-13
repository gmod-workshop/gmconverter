namespace GMConverter.SDK.Animation;

/// <summary>
/// What an animation is for, in engine-neutral terms: a run with a rifle, a turn to the left, a
/// death. Exporters map roles to the target game's own names for them, such as Source activities.
/// </summary>
/// <param name="Action">What the character is doing.</param>
/// <param name="Stance">How it holds its weapon, or null when the animation doesn't say.</param>
/// <param name="Direction">Which way it moves or turns, or where a hit came from; null when that doesn't apply.</param>
public sealed record AnimationRole(AnimationAction Action, WeaponStance? Stance = null, MoveDirection? Direction = null);
