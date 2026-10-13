using GMConverter.SDK.Profiles;
using GMConverter.SourceEngine.Audio;

namespace GMConverter.SourceEngine.Profiles;

/// <summary>
/// A Source game the sound exporter can target. Games differ in how they load sound definitions,
/// so each profile decides where and in what form they are written. The exporter finds the
/// profile by <see cref="SDK.Exporters.ExportContext.TargetGameId"/> through <see cref="SourceSoundProfiles"/>.
/// </summary>
internal interface ISourceSoundProfile : IExportGameProfile
{
    /// <summary>
    /// The definitions file to write for <paramref name="definitions"/>, relative to the output
    /// directory, and its contents.
    /// </summary>
    (string RelativePath, string Contents) WriteDefinitions(IReadOnlyList<SourceSoundDefinition> definitions, string name);
}
