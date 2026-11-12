using System.Numerics;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
using GMConverter.SourceEngine.Animation;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceBoneVisibilityTests
{
    private static readonly Skeleton _skeleton = new(
    [
        new Bone(0, "root", -1, Transform.Identity),
        new Bone(1, "hand", 0, new Transform(new Vector3(0f, 0f, 1f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f), Vector3.One)),
        new Bone(2, "bottle", 1, Transform.Identity, HiddenAtRest: true),
        new Bone(3, "cork", 2, Transform.Identity),
    ]);

    [Fact]
    public void BonesStartAsTheyRestAndFollowTheirTrack()
    {
        var clip = new AnimationClip("drink", 10f, 1f,
        [
            new BoneVisibilityTrack(2, [new VisibilityKeyframe(0.3f, true), new VisibilityKeyframe(0.8f, false)]),
        ]);

        Assert.Equal([2], SourceBoneVisibility.HiddenBones(_skeleton, clip, 0));
        Assert.Empty(SourceBoneVisibility.HiddenBones(_skeleton, clip, 3));
        Assert.Empty(SourceBoneVisibility.HiddenBones(_skeleton, clip, 7));
        Assert.Equal([2], SourceBoneVisibility.HiddenBones(_skeleton, clip, 8));
        Assert.Equal([2], SourceBoneVisibility.HiddenBones(_skeleton, new AnimationClip("idle", 30f, 0f, []), 0));
    }

    [Fact]
    public void HiddenBoneDropsStraightDownAndTakesItsChildrenAlong()
    {
        var locals = _skeleton.Bones.ToDictionary(bone => bone.Index, bone => bone.LocalBindPose);

        SourceBoneVisibility.Apply(_skeleton, locals, [2, 3]);

        // The hand is turned 90° about X (Y to Z), so straight down in model space is -Y in its frame.
        var drop = locals[2].Translation;
        Assert.True(drop.Y < -10f && MathF.Abs(drop.X) < 1e-3f && MathF.Abs(drop.Z) < 1e-3f, $"Unexpected drop {drop}.");
        Assert.Equal(Vector3.Zero, locals[3].Translation);
        Assert.Equal(_skeleton.Bones[1].LocalBindPose, locals[1]);
    }

    [Fact]
    public void ModelsWithoutHiddenBonesNeedNoHiddenIdle()
    {
        Assert.True(SourceBoneVisibility.HasHiddenBones(_skeleton));
        Assert.False(SourceBoneVisibility.HasHiddenBones(new Skeleton([new Bone(0, "root", -1, Transform.Identity)])));
    }
}
