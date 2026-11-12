namespace GMConverter.UnrealEngine.Formats.Wwise;

/// <summary>One media file a <see cref="WwiseEventSidecar"/> event picks from.</summary>
/// <param name="File">The <c>.wem</c> file, relative to the sidecar.</param>
/// <param name="Weight">How likely it is to be picked relative to the others.</param>
/// <param name="GainDb">Its volume relative to the event's, in decibels, zero or below.</param>
internal sealed record WwiseEventSidecarMedia(string File, float Weight = 1f, float GainDb = 0f);
