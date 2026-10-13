using GMConverter.SourceEngine.Profiles.GarrysMod;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>
/// The Source games the sound exporter can target. They share their ids with the MDL exporter's
/// targets in <see cref="SourceProfiles"/>, so one game choice covers a model and its sounds.
/// </summary>
internal static class SourceSoundProfiles
{
    /// <summary>Every target; the first is the sound exporter's default.</summary>
    public static IReadOnlyList<ISourceSoundProfile> All { get; } = [new GarrysModSoundProfile(), new GenericSourceSoundProfile()];

    public static ISourceSoundProfile? Find(string? id)
    {
        return id is null ? null : All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
