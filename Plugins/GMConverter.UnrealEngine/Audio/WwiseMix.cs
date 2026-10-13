namespace GMConverter.UnrealEngine.Audio;

/// <summary>
/// What one Wwise node adds to the sound passing through it. Volumes and pitches add up from a
/// sound through every container and actor mixer above it; randomizer ranges add up the same way.
/// </summary>
/// <param name="VolumeDb">Volume offset in decibels.</param>
/// <param name="VolumeMinDb">Lowest random volume offset in decibels, zero or below.</param>
/// <param name="VolumeMaxDb">Highest random volume offset in decibels.</param>
/// <param name="PitchCents">Pitch offset in cents, where 1200 is an octave.</param>
/// <param name="PitchMinCents">Lowest random pitch offset in cents.</param>
/// <param name="PitchMaxCents">Highest random pitch offset in cents.</param>
/// <param name="AttenuationMeters">How far the attenuation set on this node carries, or null when it sets none.</param>
/// <param name="Loops">Whether this node loops what it plays.</param>
internal sealed record WwiseMix(
    float VolumeDb = 0f,
    float VolumeMinDb = 0f,
    float VolumeMaxDb = 0f,
    float PitchCents = 0f,
    float PitchMinCents = 0f,
    float PitchMaxCents = 0f,
    float? AttenuationMeters = null,
    bool Loops = false)
{
    public static WwiseMix None { get; } = new();

    /// <summary>
    /// This node's mix followed by the mix of <paramref name="child"/>, a node below it: offsets
    /// add, and the attenuation nearest the sound wins, since Wwise children override their parents.
    /// </summary>
    public WwiseMix Then(WwiseMix child) => new(
        VolumeDb + child.VolumeDb,
        VolumeMinDb + child.VolumeMinDb,
        VolumeMaxDb + child.VolumeMaxDb,
        PitchCents + child.PitchCents,
        PitchMinCents + child.PitchMinCents,
        PitchMaxCents + child.PitchMaxCents,
        child.AttenuationMeters ?? AttenuationMeters,
        Loops || child.Loops);
}
