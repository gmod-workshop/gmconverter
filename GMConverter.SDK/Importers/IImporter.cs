using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;

namespace GMConverter.SDK.Importers;

/// <summary>
/// Reads a source file into a <see cref="Model"/>. Like exporters, each importer declares an
/// <see cref="OptionSchema"/> for its own settings; the host renders UI/CLI from it and hands the
/// collected values to <see cref="Parse"/> through <see cref="ModelParseOptions.Options"/>.
/// </summary>
public interface IImporter
{
    /// <summary>Stable identifier of the input format (e.g. <c>"psk"</c>, <c>"mdl"</c>).</summary>
    string InputFormat { get; }

    /// <summary>Human-readable name shown in UI dropdowns and CLI help.</summary>
    string InputName { get; }

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

    object Summarize(string inputPath);

    Model Parse(string inputPath, ModelParseOptions options);
}
