using System.Numerics;
using GMConverter.SDK.Importers;
using Vector = JeremyAnsel.Xwa.Opt.Vector;

namespace GMConverter.XWingAlliance.Importers;

internal static class CoordinateTransforms
{
    public static Vector3 ToSource(Vector vector, float scaleFactor, ModelAxisMode axisMode)
    {
        var scaled = new Vector3(vector.X * scaleFactor, vector.Y * scaleFactor, vector.Z * scaleFactor);
        return ModelAxisTransforms.TransformPosition(scaled, axisMode, "opt");
    }

    public static Vector3 ToSourceNormal(Vector vector, ModelAxisMode axisMode)
    {
        return ModelAxisTransforms.TransformNormal(new Vector3(vector.X, vector.Y, vector.Z), axisMode, "opt");
    }
}
