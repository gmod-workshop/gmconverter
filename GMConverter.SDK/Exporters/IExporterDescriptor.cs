namespace GMConverter.SDK.Exporters;

/// <summary>
/// Non-generic metadata for an exporter. Lets the host store exporters in homogeneous
/// collections (plugin registries, dropdowns) without committing to a specific options type.
/// The actual export call lives on <see cref="IExporter{TOptions}"/>.
/// </summary>
public interface IExporterDescriptor
{
    string OutputFormat { get; }

    string OutputName { get; }
}
