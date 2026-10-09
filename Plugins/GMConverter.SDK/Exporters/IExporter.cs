using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// Exports a <see cref="Model"/> to a target format. Each exporter declares an
/// <see cref="OptionSchema"/> describing the configurable options it accepts; the host uses the
/// schema to render UI controls and CLI arguments, then constructs an <see cref="OptionValues"/>
/// bag from user input and hands it to <see cref="Export"/>. Plugins read what they need from the
/// bag via the typed accessors and assemble their own internal strongly-typed options if desired.
/// </summary>
public interface IExporter
{
    /// <summary>Stable identifier of the output format (e.g. <c>"obj"</c>, <c>"glb"</c>, <c>"mdl"</c>).</summary>
    string OutputFormat { get; }

    /// <summary>Human-readable name shown in UI dropdowns and CLI help.</summary>
    string OutputName { get; }

    /// <summary>
    /// Describes the options this exporter accepts. The host renders UI/CLI from this schema and
    /// builds the <see cref="OptionValues"/> passed to <see cref="Export"/>. Return
    /// <see cref="OptionSchema.Empty"/> for exporters with no user-configurable options.
    /// </summary>
    OptionSchema OptionSchema { get; }

    /// <summary>Performs the export. <paramref name="options"/> is built by the host from the schema.</summary>
    void Export(Model model, string outputDirectory, string baseName, OptionValues options);
}
