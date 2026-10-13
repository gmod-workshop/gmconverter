using GMConverter.Core.Exporters;
using GMConverter.Core.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Importers;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// The input and output formats the host offers: plugin importers, plugin exporters, and the
/// built-in OBJ and glTF exporters, plus the format names that share an exporter. The CLI and UI
/// look formats up here instead of special-casing them.
/// </summary>
internal static class AssetFormats
{
    private static readonly string[] _builtInExportFormats = ["obj", "glb", "gltf"];
    private static readonly OBJExporter _objExporter = new();
    private static readonly GLTFExporter _gltfExporter = new();

    public static IReadOnlyList<IImporter> Importers => [.. PluginHost.Registry.Importers
        .OrderBy(importer => importer.InputFormat, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Every output format, in display order. Built from the registry on each call.</summary>
    public static IReadOnlyList<ExportFormat> Exports
    {
        get
        {
            List<ExportFormat> formats =
            [
                new("obj", "OBJ", _objExporter),
                new("glb", "GLB", _gltfExporter) { ForcedOptions = new Dictionary<string, object?> { ["binary"] = true } },
                new("gltf", "glTF", _gltfExporter) { ForcedOptions = new Dictionary<string, object?> { ["binary"] = false } },
                .. PluginHost.Registry.Exporters
                    .Where(exporter => !_builtInExportFormats.Contains(exporter.OutputFormat, StringComparer.OrdinalIgnoreCase))
                    .Select(exporter => new ExportFormat(exporter.OutputFormat, exporter.OutputFormat.ToUpperInvariant(), exporter)),
            ];
            if (PluginHost.Registry.GetExporter("mdl") is { } mdlExporter)
            {
                formats.Add(new ExportFormat("source", "Source", mdlExporter));
            }
            return formats;
        }
    }

    /// <summary>Each exporter once, as the first format that uses it.</summary>
    public static IReadOnlyList<ExportFormat> DistinctExporters => [.. Exports.DistinctBy(format => format.Exporter)];

    public static IImporter? FindImporter(string format)
    {
        return PluginHost.Registry.GetImporter(format.Trim());
    }

    public static IImporter GetImporter(string format)
    {
        return FindImporter(format)
            ?? throw new GMConverterException($"Unsupported input format: {format}. Formats are contributed by plugins: {string.Join(", ", Importers.Select(importer => importer.InputFormat))}.");
    }

    public static ExportFormat? FindExport(string format)
    {
        return Exports.FirstOrDefault(export => string.Equals(export.Format, format.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static ExportFormat GetExport(string format)
    {
        return FindExport(format)
            ?? throw new GMConverterException($"Unsupported output format or plugin not loaded: {format}");
    }

    public static string RequireInputFile(string path, IImporter importer)
    {
        var fullPath = FullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"File not found: {fullPath}");
        }

        if (!importer.FileExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new GMConverterException($"Expected a {string.Join(" or ", importer.FileExtensions)} file: {fullPath}");
        }

        return fullPath;
    }

    public static string FullPath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}
