namespace GMConverter.SourceEngine.Games;

/// <summary>The Source games the MDL exporter can target. The plugin registers these with the host.</summary>
internal static class SourceGameProfiles
{
    /// <summary>Every target; the first is the MDL exporter's default.</summary>
    public static IReadOnlyList<ISourceGameProfile> All { get; } = [new GarrysModGameProfile(), new SourceGameProfile()];

    public static ISourceGameProfile? Find(string? id)
    {
        return id is null ? null : All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
