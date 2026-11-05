using GMConverter.SourceEngine.Profiles.GarrysMod;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>
/// The Source games the particle exporter can target. They share their ids with the MDL exporter's
/// targets in <see cref="SourceProfiles"/>, so one game choice covers a model and its effects.
/// </summary>
internal static class SourceParticleProfiles
{
    /// <summary>Every target; the first is the particle exporter's default.</summary>
    public static IReadOnlyList<ISourceParticleProfile> All { get; } = [new GarrysModParticleProfile(), new GenericSourceParticleProfile()];

    public static ISourceParticleProfile? Find(string? id)
    {
        return id is null ? null : All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
