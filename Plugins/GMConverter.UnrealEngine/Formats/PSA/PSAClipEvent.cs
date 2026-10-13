using GMConverter.SDK.Animation;

namespace GMConverter.UnrealEngine.Formats.PSA;

/// <summary>An animation notify kept from an exported sequence, as the clip sidecar stores it.</summary>
/// <param name="Time">When it fires, in seconds from the start of the sequence.</param>
/// <param name="Kind">What it does.</param>
/// <param name="Sound">The sound (Wwise event or sound asset) it plays, by name.</param>
/// <param name="Bone">The socket or bone it names, such as the foot of a footstep.</param>
internal sealed record PSAClipEvent(float Time, AnimationEventKind Kind, string? Sound = null, string? Bone = null);
