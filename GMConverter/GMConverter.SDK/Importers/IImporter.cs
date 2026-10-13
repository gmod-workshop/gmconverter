using GMConverter.SDK.Assets;
using GMConverter.SDK.Options;

namespace GMConverter.SDK.Importers;

/// <summary>
/// Reads a source file into an asset. This is the part of an importer the host lists, validates
/// and renders options for; the reading itself is on the interface for the importer's
/// <see cref="Kind"/>, such as <see cref="IModelImporter"/>. Like exporters, each importer
/// declares an <see cref="OptionSchema"/> for its own settings; the host renders UI/CLI from it
/// and hands the collected values over in <see cref="ImportContext.Options"/>.
/// </summary>
public interface IImporter
{
    /// <summary>Stable identifier of the input format (e.g. <c>"psk"</c>, <c>"mdl"</c>).</summary>
    string Format { get; }

    /// <summary>Human-readable name shown in UI dropdowns and CLI help.</summary>
    string DisplayName { get; }

    /// <summary>The kind of asset this importer produces.</summary>
    AssetKind Kind { get; }

    /// <summary>
    /// File extensions this importer reads, including the leading dot (e.g. <c>".psk"</c>). The
    /// host uses them for file pickers and to reject mismatched input paths.
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Describes the options this importer accepts. Return <see cref="OptionSchema.Empty"/> for
    /// importers with no user-configurable options.
    /// </summary>
    OptionSchema OptionSchema { get; }

    /// <summary>Describes the file without fully importing it, for the host's info output.</summary>
    object Summarize(string inputPath);
}
