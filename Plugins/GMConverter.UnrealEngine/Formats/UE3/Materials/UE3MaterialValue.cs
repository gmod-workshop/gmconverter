using System.Numerics;

namespace GMConverter.UnrealEngine.Formats.UE3.Materials;

/// <summary>
/// A value flowing through a material graph: up to four components, where a single component
/// broadcasts against wider values the way Unreal's material compiler does.
/// </summary>
internal readonly record struct UE3MaterialValue(Vector4 Value, int Components)
{
    public static UE3MaterialValue Scalar(float value) => new(new Vector4(value), 1);

    public float this[int component] => Components == 1 ? Value.X : component switch
    {
        0 => Value.X,
        1 => Value.Y,
        2 => Value.Z,
        _ => Value.W,
    };

    /// <summary>The value with every component filled, broadcasting a scalar.</summary>
    public Vector4 Expanded => Components == 1 ? new Vector4(Value.X) : Value;

    public static UE3MaterialValue Combine(UE3MaterialValue a, UE3MaterialValue b, Func<Vector4, Vector4, Vector4> operation)
    {
        return new UE3MaterialValue(operation(a.Expanded, b.Expanded), Math.Max(a.Components, b.Components));
    }
}
