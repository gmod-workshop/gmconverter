namespace GMConverter.UnrealEngine.Audio;

/// <summary>How a <see cref="WwiseNode"/> plays its children.</summary>
internal enum WwiseNodeKind
{
    /// <summary>A sound playing one media file.</summary>
    Sound,

    /// <summary>A random container: one child by weight each play.</summary>
    Random,

    /// <summary>A sequence container: each child in turn.</summary>
    Sequence,

    /// <summary>A switch container, already narrowed to the branch it plays by default.</summary>
    Switch,

    /// <summary>A blend or layer container, or an event with several play actions: every child at once.</summary>
    Layer,
}
