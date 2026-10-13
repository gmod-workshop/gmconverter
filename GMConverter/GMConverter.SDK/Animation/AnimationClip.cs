namespace GMConverter.SDK.Animation;

/// <summary>
/// A named animation. <see cref="Layering"/> is set when the clip is a layer to add on top of other
/// animation, and null for a clip that plays on its own.
/// </summary>
/// <param name="Name">The clip's name.</param>
/// <param name="FrameRate">The keyframe rate in frames per second.</param>
/// <param name="DurationSeconds">The clip's length.</param>
/// <param name="Tracks">The animated bones and properties.</param>
/// <param name="Layering">How the clip is layered on other animation, or null when it plays on its own.</param>
/// <param name="RootMotion">
/// True when the skeleton root's travel and turning over the clip is the character moving through
/// the world (a walk cycle that steps forward, a turn in place) rather than part of the pose.
/// Keyframes stay as authored, so exporters that don't move characters from animation play the
/// clip as before; the others can take the root's motion out and apply it as movement.
/// </param>
/// <param name="Loops">True when the clip is meant to repeat, such as an idle or a walk cycle.</param>
/// <param name="Role">
/// What the clip is for (a rifle run, a death), when the importer or the source game's profile can
/// tell; null otherwise. Exporters use it to pick the clips game code plays for each action.
/// </param>
/// <param name="Events">Things that happen at moments in the clip, such as footsteps and sounds, in time order.</param>
public sealed record AnimationClip(
    string Name,
    float FrameRate,
    float DurationSeconds,
    IReadOnlyList<IAnimationTrack> Tracks,
    AnimationLayering? Layering = null,
    bool RootMotion = false,
    bool Loops = false,
    AnimationRole? Role = null,
    IReadOnlyList<AnimationEvent>? Events = null);
