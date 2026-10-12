using GMConverter.SDK.Options;
namespace GMConverter.SDK.Explorer;

/// <summary>
/// A browsed entry made ready for import: the file to parse, where its materials live, and any
/// importer option values the explorer resolved alongside it (e.g. an extracted animation file),
/// keyed by the importer's <see cref="OptionDescriptor.Key"/>.
/// </summary>
public sealed record ExplorerResolvedEntry(
    string InputPath,
    string MaterialDirectory,
    string? Details = null,
    IReadOnlyDictionary<string, object?>? ImporterOptions = null);
