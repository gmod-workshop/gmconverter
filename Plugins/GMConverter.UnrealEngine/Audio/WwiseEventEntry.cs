namespace GMConverter.UnrealEngine.Audio;

/// <summary>One media file a flattened Wwise event can play.</summary>
/// <param name="MediaId">The media's id.</param>
/// <param name="Weight">How likely it is to play, as a share of the event's plays.</param>
/// <param name="GainDb">Its volume relative to the event's, in decibels, zero or below.</param>
internal sealed record WwiseEventEntry(uint MediaId, float Weight, float GainDb);
