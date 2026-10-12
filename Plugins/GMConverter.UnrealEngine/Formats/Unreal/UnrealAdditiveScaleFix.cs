using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Animations.PSA;

namespace GMConverter.UnrealEngine.Formats.Unreal;

// Works around a CUE4Parse decoding bug for additive animations. Additive scale is stored as a
// difference and applied as base * (1 + difference), so a bone without scale data must decode to a
// difference of 0. CUE4Parse's per-track decoder (FUECompressedAnimData) fills in 1 instead, which
// is right for normal animations but doubles every such bone once the additive is baked onto its
// base. Source folds bone scale into child offsets, so the doubling compounds down the hierarchy
// and the model flies apart (e.g. Star Wars Zero Company's B1 aim poses). ACL-compressed additives
// are decoded correctly. Upstream CUE4Parse has the same code (Legacy/AnimConverter.cs).
//
// The bug leaves an exact signature: the baked scale is twice the base pose's scale on every frame.
// Real additive scale doubling is not a thing animators author, so those bones get the base scale.
internal static class UnrealAdditiveScaleFix
{
    private const float _tolerance = 0.0001f;

    /// <summary>Restores base-pose scale on bones the decoder doubled. Returns the number of bones fixed.</summary>
    public static int Apply(UAnimSequence sequence, USkeleton skeleton, CAnimSequence baked)
    {
        if (TryGetBaseScales(sequence, skeleton) is not { } baseScales)
        {
            return 0;
        }

        return RestoreDoubledScales([.. baked.Tracks.Select(track => track.KeyScale)], baseScales);
    }

    /// <summary>Replaces scale keys that are exactly twice the base scale on every frame.</summary>
    internal static int RestoreDoubledScales(IReadOnlyList<FVector[]> scaleKeysByBone, IReadOnlyList<FVector> baseScales)
    {
        var fixedBones = 0;
        for (var boneIndex = 0; boneIndex < scaleKeysByBone.Count && boneIndex < baseScales.Count; boneIndex++)
        {
            var keys = scaleKeysByBone[boneIndex];
            var baseScale = baseScales[boneIndex];
            if (keys.Length == 0 || !keys.All(key => IsNear(key, baseScale * 2f)))
            {
                continue;
            }

            Array.Fill(keys, baseScale);
            fixedBones++;
        }

        return fixedBones;
    }

    // Only single-pose bases are checked; per-frame bases (ABPT_AnimScaled, ABPT_LocalAnimFrame)
    // are left as CUE4Parse bakes them.
    private static FVector[]? TryGetBaseScales(UAnimSequence sequence, USkeleton skeleton)
    {
        switch (sequence.RefPoseType)
        {
            case EAdditiveBasePoseType.ABPT_RefPose:
                return [.. skeleton.ReferenceSkeleton.FinalRefBonePose.Select(pose => pose.Scale3D)];
            case EAdditiveBasePoseType.ABPT_AnimFrame
                when sequence.RefPoseSeq is { } refPoseSeq && refPoseSeq.TryLoad(out var baseExport) && baseExport is UAnimSequence baseSequence:
                return ReadFrameScales(
                    skeleton.ConvertAnims(baseSequence).Sequences[0],
                    Math.Max(0, sequence.RefFrameIndex),
                    skeleton.ReferenceSkeleton.FinalRefBonePose.Length);
            default:
                return null;
        }
    }

    private static FVector[] ReadFrameScales(CAnimSequence baseSequence, int frame, int boneCount)
    {
        var scales = new FVector[boneCount];
        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            var rotation = FQuat.Identity;
            var position = FVector.ZeroVector;
            var scale = FVector.OneVector;
            if (boneIndex < baseSequence.Tracks.Count)
            {
                baseSequence.Tracks[boneIndex].GetBoneTransform(frame, baseSequence.NumFrames, ref rotation, ref position, ref scale);
            }

            scales[boneIndex] = scale;
        }

        return scales;
    }

    private static bool IsNear(FVector value, FVector expected)
    {
        return MathF.Abs(value.X - expected.X) <= _tolerance &&
            MathF.Abs(value.Y - expected.Y) <= _tolerance &&
            MathF.Abs(value.Z - expected.Z) <= _tolerance;
    }
}
