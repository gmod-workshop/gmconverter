using GMConverter.SDK.Profiles;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>
/// A Source game the particle exporter can target. Games load particle files differently, so each
/// profile says what else to write for a <c>.pcf</c>. The exporter finds the profile by
/// <see cref="SDK.Exporters.ExportContext.TargetGameId"/> through <see cref="SourceParticleProfiles"/>.
/// </summary>
internal interface ISourceParticleProfile : IExportGameProfile
{
    /// <summary>Whether the game can run the Lua that draws mesh emitters (see <see cref="Particles.GarrysModEffectsLua"/>).</summary>
    bool DrawsMeshEmitters => false;

    /// <summary>
    /// The file that loads <paramref name="pcfPath"/> (relative to the game directory) and its
    /// systems, relative to the output directory, with its contents; or null with the step the user
    /// must take instead.
    /// </summary>
    (string? RelativePath, string Contents) WriteLoader(string pcfPath, IReadOnlyList<string> systems, string name);
}
