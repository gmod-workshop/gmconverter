using GMConverter.SDK.Audio;

namespace GMConverter.SourceEngine.Audio;

/// <summary>
/// Turns engine-neutral sound events into Source sound definitions. Source picks each play's wave
/// uniformly at random, so weights become repeated waves and sequences become random; ranges map
/// directly, and how far a sound carries becomes a sound level.
/// </summary>
internal static class SourceSoundDefinitions
{
    // Source's distance attenuation: a sound of level L (above 50 dB) fades to silence at
    // 50 * (L - 50) units, so 75 dB carries 1250 units (about 32 m).
    private const float _unitsPerDecibel = 50f;
    private const float _metersToUnits = 39.3700787f;
    private const int _minSoundLevel = 60;
    private const int _maxSoundLevel = 140;

    // Weights are rounded to repeats of the same wave; more than this many copies of one wave
    // would only make the definition longer for a difference no one hears.
    private const int _maxRepeats = 8;

    /// <param name="sound">The sound whose events to define.</param>
    /// <param name="wavePaths">Each clip's WAV path relative to the <c>sound</c> folder, by clip name.</param>
    public static IReadOnlyList<SourceSoundDefinition> Create(Sound sound, IReadOnlyDictionary<string, string> wavePaths)
    {
        List<SourceSoundDefinition> definitions = [];
        foreach (var soundEvent in sound.Events)
        {
            var waves = Waves(soundEvent, wavePaths);
            if (waves.Count == 0)
            {
                continue;
            }

            definitions.Add(new SourceSoundDefinition(
                soundEvent.Name,
                Channel(soundEvent.Category),
                Math.Clamp(soundEvent.Volume.Min, 0f, 1f),
                Math.Clamp(soundEvent.Volume.Max, 0f, 1f),
                Pitch(soundEvent.Pitch.Min),
                Pitch(soundEvent.Pitch.Max),
                SoundLevel(soundEvent.AttenuationMeters),
                waves));
        }

        return definitions;
    }

    public static string Channel(SoundCategory category) => category switch
    {
        SoundCategory.Weapon => "CHAN_WEAPON",
        SoundCategory.Voice => "CHAN_VOICE",
        SoundCategory.Footstep or SoundCategory.Movement => "CHAN_BODY",
        SoundCategory.Ambient or SoundCategory.Music or SoundCategory.UI => "CHAN_STATIC",
        _ => "CHAN_AUTO",
    };

    /// <summary>The sound level that carries about <paramref name="meters"/>, or 0 (heard everywhere) for null.</summary>
    public static int SoundLevel(float? meters)
    {
        if (meters is not { } distance)
        {
            return 0;
        }

        var level = 50f + distance * _metersToUnits / _unitsPerDecibel;
        return Math.Clamp((int)MathF.Round(level), _minSoundLevel, _maxSoundLevel);
    }

    private static int Pitch(float ratio) => Math.Clamp((int)MathF.Round(ratio * 100f), 1, 255);

    private static List<string> Waves(SoundEvent soundEvent, IReadOnlyDictionary<string, string> wavePaths)
    {
        var entries = soundEvent.Entries
            .Where(entry => entry.Weight > 0f && wavePaths.ContainsKey(entry.Clip))
            .ToList();
        if (entries.Count == 0)
        {
            return [];
        }

        var lightest = entries.Min(entry => entry.Weight);
        List<string> waves = [];
        foreach (var entry in entries)
        {
            var repeats = Math.Clamp((int)MathF.Round(entry.Weight / lightest), 1, _maxRepeats);
            waves.AddRange(Enumerable.Repeat(wavePaths[entry.Clip], repeats));
        }

        return waves;
    }
}
