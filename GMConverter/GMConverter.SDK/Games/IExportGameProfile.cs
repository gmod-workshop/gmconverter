using GMConverter.SDK.Geometry;
using GMConverter.SDK.Options;

namespace GMConverter.SDK.Games;

/// <summary>
/// A game assets are converted for, offered as a target of one exporter. Choosing it lets the
/// exporter's plugin follow that game's conventions, and lets this profile adjust the model first.
/// The exporter sees the choice as <see cref="Exporters.ExportContext.TargetGameId"/>.
/// </summary>
public interface IExportGameProfile : IGameProfile
{
    /// <summary>The <see cref="Exporters.IExporter.Format"/> of the exporter this game is a target of, e.g. <c>"mdl"</c>.</summary>
    string ExporterFormat { get; }

    /// <summary>
    /// Options that only apply when this game is the target. The host renders them next to the
    /// exporter's own options and passes their values to <see cref="Prepare"/> and the exporter.
    /// </summary>
    OptionSchema OptionSchema { get; }

    /// <summary>
    /// Adjusts the model for this game before it is exported, such as adding attachments the
    /// game's code looks up by name. <paramref name="options"/> holds this profile's options with
    /// defaults filled in.
    /// </summary>
    Model Prepare(Model model, OptionValues options) => model;
}
