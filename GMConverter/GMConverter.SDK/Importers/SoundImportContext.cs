namespace GMConverter.SDK.Importers;

/// <summary>What a sound importer is asked to read.</summary>
/// <param name="InputPath">Full path of the file to read.</param>
public sealed record SoundImportContext(string InputPath) : ImportContext(InputPath);
