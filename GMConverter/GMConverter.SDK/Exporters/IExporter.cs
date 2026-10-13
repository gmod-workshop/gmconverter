using GMConverter.SDK.Assets;
using GMConverter.SDK.Options;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// Writes an asset to a target format. This is the part of an exporter the host lists and
/// renders options for; the writing itself is on the interface for the exporter's
/// <see cref="Kind"/>, such as <see cref="IModelExporter"/>. Each exporter declares an
/// <see cref="OptionSchema"/> describing the configurable options it accepts; the host uses the
/// schema to render UI controls and CLI arguments, then hands the collected values over in
/// <see cref="ExportContext.Options"/>. Plugins read what they need from the bag via the typed
/// accessors and assemble their own internal strongly-typed options if desired.
/// </summary>
public interface IExporter
{
    /// <summary>Stable identifier of the output format (e.g. <c>"obj"</c>, <c>"glb"</c>, <c>"mdl"</c>).</summary>
    string Format { get; }

    /// <summary>Human-readable name shown in UI dropdowns and CLI help.</summary>
    string DisplayName { get; }

    /// <summary>The kind of asset this exporter writes.</summary>
    AssetKind Kind { get; }

    /// <summary>
    /// Describes the options this exporter accepts. The host renders UI/CLI from this schema and
    /// builds the <see cref="ExportContext.Options"/> passed to the export. Return
    /// <see cref="OptionSchema.Empty"/> for exporters with no user-configurable options.
    /// </summary>
    OptionSchema OptionSchema { get; }
}
