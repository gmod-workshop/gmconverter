using GMConverter.SDK.Exporters;
using GMConverter.SDK.Options;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// An output format the host offers. Several formats can share one exporter: <c>glb</c> and
/// <c>gltf</c> both use the glTF exporter with <see cref="ForcedOptions"/> choosing binary or text,
/// and <c>source</c> is another name for <c>mdl</c>.
/// </summary>
/// <param name="Format">The name users select, e.g. <c>"gltf"</c>.</param>
/// <param name="Label">Short display label.</param>
/// <param name="Exporter">The exporter that writes this format.</param>
internal sealed record ExportFormat(string Format, string Label, IExporter Exporter)
{
    /// <summary>Exporter options this format sets itself, overriding whatever the user chose.</summary>
    public IReadOnlyDictionary<string, object?> ForcedOptions { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// The key the format's option values are kept under. A plain alias shares its exporter's
    /// values (<c>source</c> uses <c>mdl</c>'s); a format that forces options keeps its own.
    /// </summary>
    public string OptionsKey => ForcedOptions.Count == 0 ? Exporter.OutputFormat : Format;

    /// <summary>The exporter's schema without <see cref="ForcedOptions"/>, which users cannot change.</summary>
    public OptionSchema OptionSchema => ForcedOptions.Count == 0
        ? Exporter.OptionSchema
        : new OptionSchema([.. Exporter.OptionSchema.Groups.Select(group => group with
        {
            Options = [.. group.Options.Where(option => !ForcedOptions.ContainsKey(option.Key))]
        })]);

    public OptionValues ApplyForcedOptions(OptionValues options)
    {
        if (ForcedOptions.Count == 0)
        {
            return options;
        }

        var values = new Dictionary<string, object?>(options.AsDictionary());
        foreach (var (key, value) in ForcedOptions)
        {
            values[key] = value;
        }
        return new OptionValues(values);
    }
}
