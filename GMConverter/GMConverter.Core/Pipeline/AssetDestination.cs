using GMConverter.SDK.Common;

namespace GMConverter.Core.Pipeline;

/// <summary>Where and how a request's asset is written, resolved before the asset is imported.</summary>
/// <param name="Format">The output format.</param>
/// <param name="Directory">Full path of the output directory.</param>
/// <param name="BaseName">Base name of the written files.</param>
internal sealed record AssetDestination(ExportFormat Format, string Directory, string BaseName)
{
    public static AssetDestination Resolve(AssetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputFormat))
        {
            throw new GMConverterException("An output format is required.");
        }

        var format = AssetFormats.GetExport(request.OutputFormat);
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new GMConverterException($"Output path is required for {format.Format} output.");
        }

        var baseName = string.IsNullOrWhiteSpace(request.BaseName)
            ? Path.GetFileNameWithoutExtension(request.InputPath)
            : request.BaseName;
        return new AssetDestination(format, AssetFormats.FullPath(request.OutputDirectory), baseName);
    }
}
