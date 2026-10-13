namespace GMConverter.SourceEngine.Animation;

/// <summary>A QC animation event: <c>{ event Name frame "Options" }</c> inside a sequence.</summary>
/// <param name="Name">The event, e.g. <c>AE_CL_PLAYSOUND</c>.</param>
/// <param name="TimeSeconds">When it fires; the exporter turns it into a frame.</param>
/// <param name="Options">The event's text argument, such as the sound to play.</param>
internal sealed record SourceAnimationEvent(string Name, float TimeSeconds, string Options = "");
