using GMConverter.SourceEngine.Profiles.GarrysMod;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>The Source games the MDL exporter can target. The plugin registers these with the host.</summary>
internal static class SourceProfiles
{
    /// <summary>Every target; the first is the MDL exporter's default.</summary>
    public static IReadOnlyList<ISourceProfile> All { get; } = [new GarrysModProfile(), new GenericSourceProfile()];

    public static ISourceProfile? Find(string? id)
    {
        return id is null ? null : All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
