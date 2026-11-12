using System.Buffers.Binary;
using System.Numerics;
using AssetsTools.NET.Extra;
using GMConverter.SDK.Geometry;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Models;
using GMConverter.Unity.Scenes;

namespace GMConverter.Unity.Profiles.LiarsBar;

/// <summary>
/// Liar's Bar moves mouths in code, not with clips or blend shapes: a <c>FaceAnimator</c> script on
/// each character turns a <c>Mouth</c> node at the head, which carries the jaw bone, about its Z
/// axis by up to its open angle (-7.242° for most characters, -10° for Dubai) as the voice it
/// listens to gets louder. The build has no type trees, so the script's fields are read by their
/// layout: after the MonoBehaviour header, a reference, the sample window, the voice's audio source,
/// five floats of which the fourth is the open angle in degrees, the mouth node, and the sensitivity.
/// </summary>
internal static class LiarsBarMouth
{
    public const string MorphName = "mouth_open";

    private const string _scriptName = "FaceAnimator";
    private const int _angleOffset = 40;
    private const int _mouthOffset = 48;
    private const int _fieldsSize = 64;
    private const float _maxAngle = 90f;

    /// <summary>The mouth the character's face animator opens, as a morph to bake, or null when it has none.</summary>
    public static UnityPoseMorph? Find(UnityAssetLibrary library, UnityHierarchy hierarchy)
    {
        foreach (var node in hierarchy.Nodes)
        {
            foreach (var script in hierarchy.GetComponents(node, AssetClassID.MonoBehaviour))
            {
                if (library.Resolve(script.File, script["m_Script"])?["m_ClassName"].AsString == _scriptName &&
                    Read(UnityAssetLibrary.ReadRawData(script), script.FileName, hierarchy) is { } morph)
                {
                    return morph;
                }
            }
        }

        return null;
    }

    private static UnityPoseMorph? Read(byte[] data, string fileName, UnityHierarchy hierarchy)
    {
        // m_GameObject (PPtr), m_Enabled (aligned), m_Script (PPtr), m_Name (aligned string).
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28));
        var fields = 32 + ((nameLength + 3) & ~3);
        if (nameLength < 0 || data.Length < fields + _fieldsSize)
        {
            return null;
        }

        var angle = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(fields + _angleOffset));
        var mouthFile = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(fields + _mouthOffset));
        var mouthPathId = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(fields + _mouthOffset + 4));
        if (!float.IsFinite(angle) || angle == 0f || MathF.Abs(angle) > _maxAngle || mouthFile != 0 ||
            hierarchy.Find(new UnityObjectKey(fileName, mouthPathId)) is not { } mouth)
        {
            return null;
        }

        return new UnityPoseMorph(MorphName, MorphTargetRole.MouthOpen, mouth, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle * MathF.PI / 180f));
    }
}
