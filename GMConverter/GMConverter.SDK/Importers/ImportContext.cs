using GMConverter.SDK.Options;

namespace GMConverter.SDK.Importers;

/// <summary>
/// What an importer is asked to read. Each asset kind adds its own settings in a derived context,
/// such as <see cref="ModelImportContext"/>.
/// </summary>
/// <param name="InputPath">Full path of the file to read.</param>
public abstract record ImportContext(string InputPath)
{
    /// <summary>Values for the importer's own <see cref="IImporter.OptionSchema"/>, keyed by option key.</summary>
    public OptionValues Options { get; init; } = OptionValues.Empty;
}
