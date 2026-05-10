using System.Numerics;
using GMConverter.Geometry;

namespace GMConverter.Importers;

internal interface IImporter
{
    string InputFormat { get; }

    object Summarize(string inputPath);

    Model Parse(string inputPath, ModelParseOptions options);
}

internal sealed record ModelParseOptions(
    float ScaleFactor,
    ModelAxisMode AxisMode = ModelAxisMode.Auto,
    MaterialResolveOptions? Materials = null,
    string? AnimationPath = null);

internal sealed record MaterialResolveOptions(string SearchDirectory);

internal enum ModelAxisMode
{
    Auto,
    ZUp,
    YUp
}

internal static class ModelAxisTransforms
{
    public static Vector3 TransformPosition(Vector3 position, ModelAxisMode axisMode, string inputFormat)
    {
        return Resolve(axisMode, inputFormat) switch
        {
            ResolvedModelAxisMode.ZUp => position,
            ResolvedModelAxisMode.YUp => new Vector3(position.X, position.Z, position.Y),
            _ => position
        };
    }

    public static Vector3 TransformNormal(Vector3 normal, ModelAxisMode axisMode, string inputFormat)
    {
        return Resolve(axisMode, inputFormat) switch
        {
            ResolvedModelAxisMode.ZUp => normal,
            ResolvedModelAxisMode.YUp => new Vector3(normal.X, normal.Z, normal.Y),
            _ => normal
        };
    }

    public static Vector3 TransformScale(Vector3 scale, ModelAxisMode axisMode, string inputFormat)
    {
        return Resolve(axisMode, inputFormat) switch
        {
            ResolvedModelAxisMode.ZUp => scale,
            ResolvedModelAxisMode.YUp => new Vector3(scale.X, scale.Z, scale.Y),
            _ => scale
        };
    }

    private static ResolvedModelAxisMode Resolve(ModelAxisMode axisMode, string inputFormat)
    {
        return axisMode switch
        {
            ModelAxisMode.ZUp => ResolvedModelAxisMode.ZUp,
            ModelAxisMode.YUp => ResolvedModelAxisMode.YUp,
            _ => ResolvedModelAxisMode.ZUp
        };
    }

    private enum ResolvedModelAxisMode
    {
        ZUp,
        YUp
    }
}
