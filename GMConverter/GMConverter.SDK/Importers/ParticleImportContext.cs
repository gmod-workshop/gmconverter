namespace GMConverter.SDK.Importers;

/// <summary>What a particle importer is asked to read.</summary>
/// <param name="InputPath">Full path of the file to read.</param>
public sealed record ParticleImportContext(string InputPath) : ImportContext(InputPath);
