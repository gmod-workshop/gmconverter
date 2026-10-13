using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SourceEngine.Profiles.GarrysMod;

namespace GMConverter.SourceEngine.Tests;

public sealed class EyesAttachmentTests
{
    [Fact]
    public void EyesSitAtTheHeadAndFaceTheModelsFront()
    {
        var model = CreateModel(-Vector3.UnitY);

        var prepared = new GarrysModProfile().Prepare(model, OptionValues.Empty);

        var eyes = prepared.Attachments!.Single(attachment => attachment.Name == EyesAttachment.Name);
        Assert.Equal(2, eyes.BoneIndex);
        var world = EyesWorld(prepared.Skeleton!, eyes);
        AssertNear(new Vector3(0, 0, 1.7f), world.Translation);
        // The eyes' forward axis (+X) points where the model faces, whatever the head bone's own rotation.
        AssertNear(-Vector3.UnitY, Vector3.TransformNormal(Vector3.UnitX, world));
        AssertNear(Vector3.UnitZ, Vector3.TransformNormal(Vector3.UnitZ, world));
    }

    [Fact]
    public void ModelsWithoutAFacingLookAlongPlusX()
    {
        var prepared = new GarrysModProfile().Prepare(CreateModel(null), OptionValues.Empty);

        var eyes = prepared.Attachments!.Single(attachment => attachment.Name == EyesAttachment.Name);
        AssertNear(Vector3.UnitX, Vector3.TransformNormal(Vector3.UnitX, EyesWorld(prepared.Skeleton!, eyes)));
    }

    [Fact]
    public void ExistingEyesHeadlessModelsAndTheOptionLeaveModelsAlone()
    {
        var existing = new Attachment("Eyes", 1, Transform.Identity);
        var withEyes = CreateModel(null) with { Attachments = [existing] };
        Assert.Same(existing, Assert.Single(EyesAttachment.AddIfMissing(withEyes).Attachments!));

        var headless = new Model("prop", [], [new Material("m")], new Skeleton([new Bone(0, "root", -1, Transform.Identity)]));
        Assert.Same(headless, EyesAttachment.AddIfMissing(headless));

        var off = new OptionValues(new Dictionary<string, object?> { [GarrysModProfile.EyesOption] = false, [GarrysModProfile.HandAttachmentsOption] = false });
        var model = CreateModel(null);
        Assert.Same(model, new GarrysModProfile().Prepare(model, off));
    }

    [Fact]
    public void HeadTargetBonesAreNotHeads()
    {
        var skeleton = new Skeleton([new Bone(0, "root", -1, Transform.Identity), new Bone(1, "head_target", 0, Transform.Identity)]);
        var model = new Model("m", [], [new Material("m")], skeleton);

        Assert.Same(model, EyesAttachment.AddIfMissing(model));
    }

    // A root, a spine 1 m up, and a head another 0.7 m up and rolled 90 degrees, as Unreal heads
    // often are, so the eyes can't just copy the head bone's axes.
    private static Model CreateModel(Vector3? forward)
    {
        var skeleton = new Skeleton(
        [
            new Bone(0, "root", -1, Transform.Identity),
            new Bone(1, "spine", 0, new Transform(Vector3.UnitZ, Quaternion.Identity, Vector3.One)),
            new Bone(2, "head", 1, new Transform(new Vector3(0, 0, 0.7f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), Vector3.One)),
        ]);
        return new Model("b1", [], [new Material("m")], skeleton, Forward: forward);
    }

    private static Matrix4x4 EyesWorld(Skeleton skeleton, Attachment eyes)
    {
        var world = Matrix(eyes.LocalTransform);
        for (var bone = skeleton.Bones[eyes.BoneIndex!.Value]; ; bone = skeleton.Bones[bone.ParentIndex])
        {
            world *= Matrix(bone.LocalBindPose);
            if (bone.ParentIndex < 0)
            {
                return world;
            }
        }
    }

    private static Matrix4x4 Matrix(Transform transform) =>
        Matrix4x4.CreateFromQuaternion(transform.Rotation) * Matrix4x4.CreateTranslation(transform.Translation);

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"Expected {expected}, got {actual}");
    }
}
