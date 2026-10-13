namespace GMConverter.SDK.Animation;

/// <summary>
/// Something that happens at a moment in a clip: a footstep, a sound, a weapon firing. Exporters
/// write events the target game understands and skip the rest.
/// </summary>
/// <param name="TimeSeconds">When it happens, from the start of the clip.</param>
/// <param name="Kind">What happens.</param>
/// <param name="Sound">
/// For <see cref="AnimationEventKind.Sound"/>, the name of the sound to play. It is the sound's name
/// in the source game, which sound exporters keep, so events and exported sounds match.
/// </param>
/// <param name="Bone">The bone or socket it happens at (such as the foot for a footstep), when known.</param>
public sealed record AnimationEvent(float TimeSeconds, AnimationEventKind Kind, string? Sound = null, string? Bone = null);
