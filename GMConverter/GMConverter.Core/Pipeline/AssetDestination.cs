using GMConverter.Core.Formats;
using GMConverter.Core.Profiles;
using GMConverter.SDK.Common;
using GMConverter.SDK.Profiles;

namespace GMConverter.Core.Pipeline;

/// <summary>Where and how a request's asset is written, resolved before the asset is imported.</summary>
/// <param name="Format">The output format.</param>
/// <param name="Directory">Full path of the output directory.</param>
/// <param name="BaseName">Base name of the written files.</param>
/// <param name="Target">The game exported for, or null when the exporter has no targets.</param>
internal sealed record AssetDestination(ExportFormat Format, string Directory, string BaseName, IExportGameProfile? Target)
{
    public static AssetDestination Resolve(AssetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputFormat))
        {
            throw new GMConverterException("An output format is required.");
        }

        var format = FormatCatalog.GetExport(request.OutputFormat);
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new GMConverterException($"Output path is required for {format.Format} output.");
        }

        var baseName = string.IsNullOrWhiteSpace(request.BaseName)
            ? Path.GetFileNameWithoutExtension(request.InputPath)
            : request.BaseName;
        var target = GameCatalog.ResolveTarget(format.Exporter.Format, request.TargetGameId);
        return new AssetDestination(format, FormatCatalog.FullPath(request.OutputDirectory), baseName, target);
    }
}
