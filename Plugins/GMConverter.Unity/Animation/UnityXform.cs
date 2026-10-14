using System.Numerics;
using AssetsTools.NET;
using GMConverter.Unity.Geometry;

namespace GMConverter.Unity.Animation;

/// <summary>A Mecanim <c>xform</c>: translation, rotation and scale in Unity space.</summary>
internal readonly record struct UnityXform(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public static UnityXform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public static UnityXform Read(AssetTypeValueField xform)
    {
        return new UnityXform(
            UnitySpace.ReadVector3(xform["t"]),
            Quaternion.Normalize(UnitySpace.ReadQuaternion(xform["q"])),
            UnitySpace.ReadVector3(xform["s"]));
    }

    /// <summary>This transform applied after <paramref name="parent"/>, as Unity combines a child with its parent.</summary>
    public UnityXform Under(UnityXform parent)
    {
        return new UnityXform(
            parent.Translation + Vector3.Transform(Translation * parent.Scale, parent.Rotation),
            Quaternion.Normalize(parent.Rotation * Rotation),
            parent.Scale * Scale);
    }
}
