namespace GMConverter.SourceEngine.Audio;

/// <summary>
/// One sound as Source defines it, whether in a sound script or a Garry's Mod <c>sound.Add</c> call.
/// </summary>
/// <param name="Name">The name games play it by.</param>
/// <param name="Channel">The <c>CHAN_*</c> channel it plays on.</param>
/// <param name="VolumeMin">The quietest gain, from 0 to 1.</param>
/// <param name="VolumeMax">The loudest gain, from 0 to 1.</param>
/// <param name="PitchMin">The lowest pitch in percent, where 100 is as recorded.</param>
/// <param name="PitchMax">The highest pitch in percent.</param>
/// <param name="SoundLevel">The sound level in decibels, or 0 for a sound heard everywhere.</param>
/// <param name="Waves">Paths of the WAVs it picks from at random, relative to the <c>sound</c> folder.</param>
internal sealed record SourceSoundDefinition(
    string Name,
    string Channel,
    float VolumeMin,
    float VolumeMax,
    int PitchMin,
    int PitchMax,
    int SoundLevel,
    IReadOnlyList<string> Waves);
