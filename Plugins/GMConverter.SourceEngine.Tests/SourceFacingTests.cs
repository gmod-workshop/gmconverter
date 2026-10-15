using System.Numerics;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SourceEngine.Exporters;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceFacingTests
{
    [Fact]
    public void ModelsFacingMinusYTurnToFacePlusX()
    {
        var model = CreateModel(-Vector3.UnitY);

        var turned = SourceFacing.FaceForward(model);

        Assert.Equal(Vector3.UnitX, turned.Forward);
        // The front of the model (-Y) ends up at +X, and its normal turns with it.
        AssertNear(Vector3.UnitX, turned.Meshes[0].Vertices[0].Position);
        AssertNear(Vector3.UnitX, turned.Meshes[0].Vertices[0].Normal);
        AssertNear(Vector3.UnitX, turned.Meshes[0].MorphTargets![0].Deltas[0].Position);
        // The root bone turns; its child keeps its bone-relative pose.
        AssertNear(new Vector3(2, 0, 0), turned.Skeleton!.Bones[0].LocalBindPose.Translation);
        Assert.Equal(model.Skeleton!.Bones[1], turned.Skeleton.Bones[1]);
        // The root's animation (walking along -Y) now walks along +X.
        var root = Assert.IsType<BoneTransformTrack>(turned.Animations![0].Tracks[0]);
        AssertNear(new Vector3(3, 0, 0), root.Keyframes[1].Transform.Translation);
        Assert.Same(model.Animations![0].Tracks[1], turned.Animations[0].Tracks[1]);
        // Model-space attachments turn; bone attachments follow their bone.
        AssertNear(new Vector3(4, 0, 0), turned.Attachments![0].LocalTransform.Translation);
        Assert.Same(model.Attachments![1], turned.Attachments[1]);
    }

    [Fact]
    public void LayerBasePosesTurnOnceAndStayShared()
    {
        var basePose = new AnimationPose("ref", new Dictionary<int, Transform>
        {
            [0] = new(new Vector3(0, -1, 0), Quaternion.Identity, Vector3.One),
            [1] = Transform.Identity,
        });
        var layering = new AnimationLayering(LayerSpace.Local, basePose);
        var model = CreateModel(-Vector3.UnitY) with
        {
            Animations = [new AnimationClip("a", 30f, 0f, [], layering), new AnimationClip("b", 30f, 0f, [], layering)],
        };

        var turned = SourceFacing.FaceForward(model);

        var turnedBase = turned.Animations![0].Layering!.Base!;
        Assert.Same(turnedBase, turned.Animations[1].Layering!.Base);
        AssertNear(Vector3.UnitX, turnedBase.BoneTransforms[0].Translation);
        Assert.Equal(Transform.Identity, turnedBase.BoneTransforms[1]);
    }

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 0f, 1f)]
    public void ModelsAlreadyFacingPlusXOrWithoutAFacingAreUnchanged(float x, float y, float z)
    {
        var model = CreateModel(new Vector3(x, y, z));

        Assert.Same(model, SourceFacing.FaceForward(model));
        var unknown = CreateModel(null);
        Assert.Same(unknown, SourceFacing.FaceForward(unknown));
    }

    private static Model CreateModel(Vector3? forward)
    {
        var mesh = new Mesh(
            [new Vertex(new Vector3(0, -1, 0), new Vector3(0, -1, 0), Vector2.Zero)],
            [new Submesh("m", [])],
            MorphTargets: [new MorphTarget("m", [new MorphTargetDelta(0, new Vector3(0, -1, 0), Vector3.Zero)])]);
        var skeleton = new Skeleton(
        [
            new Bone(0, "root", -1, new Transform(new Vector3(0, -2, 0), Quaternion.Identity, Vector3.One)),
            new Bone(1, "child", 0, new Transform(new Vector3(0, -1, 0), Quaternion.Identity, Vector3.One)),
        ]);
        var walk = new AnimationClip("walk", 30f, 1f,
        [
            new BoneTransformTrack(0,
            [
                new TransformKeyframe(0f, Transform.Identity),
                new TransformKeyframe(1f, new Transform(new Vector3(0, -3, 0), Quaternion.Identity, Vector3.One)),
            ]),
            new BoneTransformTrack(1, [new TransformKeyframe(0f, Transform.Identity)]),
        ]);
        return new Model("m", [mesh], [new Material("m")], skeleton, [walk],
            Attachments:
            [
                new Attachment("world", null, new Transform(new Vector3(0, -4, 0), Quaternion.Identity, Vector3.One)),
                new Attachment("hand", 1, Transform.Identity),
            ],
            Forward: forward);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"Expected {expected}, got {actual}");
    }
}
