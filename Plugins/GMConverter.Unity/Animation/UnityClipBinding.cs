using AssetsTools.NET;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Animation;

/// <summary>
/// What one or more consecutive curves of a clip drive. <see cref="Path"/> is the CRC32 of the
/// target's transform path below the animated root; <see cref="Attribute"/> says which property,
/// or for humanoid curves (<see cref="IsHumanoid"/>) which muscle, root or goal value.
/// </summary>
internal sealed record UnityClipBinding(uint Path, uint Attribute, int ClassId, int FirstCurve)
{
    private const int _gameObjectClass = 1;
    private const int _transformClass = 4;
    private const int _animatorClass = 95;

    private static readonly uint _isActive = UnityPathHash.Compute("m_IsActive");

    public bool IsHumanoid => ClassId == _animatorClass && Path == 0;

    /// <summary>Whether the curve turns a game object on (above 0.5) and off.</summary>
    public bool IsActiveToggle => ClassId == _gameObjectClass && Attribute == _isActive;

    public UnityTransformCurve TransformCurve => ClassId == _transformClass
        ? Attribute switch
        {
            1 => UnityTransformCurve.Position,
            2 => UnityTransformCurve.Rotation,
            3 => UnityTransformCurve.Scale,
            4 => UnityTransformCurve.Euler,
            _ => UnityTransformCurve.None,
        }
        : UnityTransformCurve.None;

    /// <summary>The number of curves the binding takes: a quaternion takes four, vectors three, anything else one.</summary>
    public int CurveCount => TransformCurve switch
    {
        UnityTransformCurve.Rotation => 4,
        UnityTransformCurve.Position or UnityTransformCurve.Scale or UnityTransformCurve.Euler => 3,
        _ => 1,
    };

    public static IReadOnlyList<UnityClipBinding> Read(AssetTypeValueField bindingConstant)
    {
        List<UnityClipBinding> bindings = [];
        var curve = 0;
        foreach (var binding in UnityFields.Items(bindingConstant["genericBindings"]))
        {
            var read = new UnityClipBinding(binding["path"].AsUInt, binding["attribute"].AsUInt, binding["typeID"].AsInt, curve);
            bindings.Add(read);
            curve += read.CurveCount;
        }

        return bindings;
    }
}
