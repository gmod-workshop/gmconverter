using System.Numerics;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;
using GMConverter.SourceEngine.Profiles.GarrysMod;

namespace GMConverter.SourceEngine.Tests;

public sealed class HumanoidRetargeterTests
{
    private static readonly Quaternion _twist = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    [Fact]
    public void ArmsFollowTheSourcePoseAndHeldItemsFollowTheHand()
    {
        // The left upper arm swings a quarter turn to the left (about Z) in the clip.
        var swing = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var source = SourceModel(swing);
        var target = TargetSkeleton();

        var retargeted = HumanoidRetargeter.RetargetArms(source, target);

        // The target's own bones come first and are kept for the hands to follow; the held bones hang from its hand.
        var bones = retargeted.Skeleton!.Bones;
        Assert.All(bones.Take(target.Bones.Count), bone => Assert.True(bone.Shared));
        Assert.Equal(["Chest", "L_Clavicle", "L_UpperArm", "L_Forearm", "L_Hand", "R_Clavicle", "R_UpperArm", "R_Forearm", "R_Hand", "Held", "Knife"], bones.Select(bone => bone.Name));
        Assert.Equal(8, bones[9].ParentIndex);

        // Only what the arms hold is left: the knife's vertex, not the arm's.
        var mesh = Assert.Single(retargeted.Meshes);
        var vertex = Assert.Single(mesh.Vertices);
        Assert.Equal(10, vertex.BoneWeights![0].BoneIndex);

        // Target arms are twice as long, so the source is scaled by two about the eye: the swung
        // left arm points along +Y from the shoulder, now 0.8 long, despite the target's twisted axes.
        var world = Pose(retargeted, retargeted.Animations![0]);
        var shoulder = world[2].Translation;
        var hand = world[4].Translation;
        AssertNear(new Vector3(0, 0.8f, 0), hand - shoulder);
        AssertNear(new Vector3(0, 0.4f, 0.4f), shoulder);

        // The knife sits where it did, relative to its (unmoved) right hand, scaled.
        AssertNear(world[8].Translation + new Vector3(0.1f, 0, 0), world[10].Translation);
    }

    [Fact]
    public void HeldItemsBecomeAModelHangingFromTheHand()
    {
        var retargeted = HumanoidRetargeter.RetargetArms(SourceModel(Quaternion.Identity), TargetSkeleton());

        var held = HumanoidRetargeter.ExtractHeld(retargeted, HumanoidBone.RightHand);

        Assert.Equal(["R_Hand", "Held", "Knife"], held.Skeleton!.Bones.Select(bone => bone.Name));
        Assert.Equal([-1, 0, 1], held.Skeleton.Bones.Select(bone => bone.ParentIndex));
        Assert.True(held.Skeleton.Bones[0].Shared);
        Assert.Null(held.Animations);
        Assert.False(held.FirstPerson);
        // With no idle clip, the first clip's grip places the knife against the hand's bind pose;
        // the vertex is now weighted to the knife's new index.
        var vertex = Assert.Single(Assert.Single(held.Meshes).Vertices);
        var bind = Pose(retargeted, new AnimationClip("bind", 30f, 0f, []));
        var grip = Pose(retargeted, retargeted.Animations![0]);
        var (hand, knife) = (8, 10);
        Assert.True(Matrix4x4.Invert(bind[knife], out var unbind));
        Assert.True(Matrix4x4.Invert(grip[hand], out var ungrip));
        var expected = Vector3.Transform(Assert.Single(retargeted.Meshes).Vertices[0].Position, unbind * grip[knife] * ungrip * bind[hand]);
        AssertNear(expected, vertex.Position);
        Assert.Equal(2, vertex.BoneWeights![0].BoneIndex);
    }

    [Fact]
    public void HeldItemsSitWhereTheIdleClipHoldsThem()
    {
        Bone B(int index, string name, int parent, Vector3 offset, HumanoidBone? role) =>
            new(index, name, parent, new Transform(offset, Quaternion.Identity, Vector3.One), role);
        var skeleton = new Skeleton(
        [
            B(0, "R_Hand", -1, new Vector3(1, 0, 0), HumanoidBone.RightHand),
            B(1, "Held", 0, new Vector3(0.05f, 0, 0), HumanoidBone.RightHeldItem),
            B(2, "Knife", 1, new Vector3(0, 0, 0.1f), null),
        ]);
        var mesh = new Mesh(
            [new Vertex(new Vector3(1.05f, 0, 0.1f), Vector3.UnitX, Vector2.Zero, [new VertexBoneWeight(2, 1f)])],
            [new Submesh("knife", [new Triangle(0, 0, 0)])]);
        // Idle holds the item 2 cm up the palm, turned a quarter about Z; the attack clip listed
        // first holds it elsewhere and must not be used.
        var grip = new Transform(new Vector3(0.02f, 0.01f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One);
        AnimationClip Clip(string name, AnimationAction action, Transform held) =>
            new(name, 30f, 0f, [new BoneTransformTrack(1, [new TransformKeyframe(0f, held)])], Role: new AnimationRole(action));
        var model = new Model("view", [mesh], [new Material("knife")], skeleton,
            [Clip("fire", AnimationAction.Attack, grip with { Translation = new Vector3(0.5f, 0, 0) }), Clip("idle", AnimationAction.Idle, grip)]);

        var held = HumanoidRetargeter.ExtractHeld(model, HumanoidBone.RightHand);

        AssertNear(grip.Translation, held.Skeleton!.Bones[1].LocalBindPose.Translation);
        var vertex = Assert.Single(Assert.Single(held.Meshes).Vertices);
        // Knife origin: hand (1, 0, 0) + grip offset; its 0.1 Z offset is unaffected by the Z turn.
        AssertNear(new Vector3(1.02f, 0.01f, 0.1f), vertex.Position);
        AssertNear(Vector3.UnitY, vertex.Normal);
    }

    [Fact]
    public void HeldItemsMoveOntoTheGamesHandle()
    {
        Bone B(int index, string name, int parent, Vector3 offset, HumanoidBone? role) =>
            new(index, name, parent, new Transform(offset, Quaternion.Identity, Vector3.One), role);
        var skeleton = new Skeleton(
        [
            B(0, "R_Hand", -1, Vector3.Zero, HumanoidBone.RightHand),
            B(1, "R_Index1", 0, new Vector3(0.1f, 0, 0.01f), HumanoidBone.RightIndex1),
            B(2, "R_Pinky1", 0, new Vector3(0.1f, 0, -0.01f), HumanoidBone.RightPinky1),
            B(3, "Held", 0, Vector3.Zero, HumanoidBone.RightHeldItem),
        ]);
        // A stick along Z through the middle of the palm, 5 cm from the wrist.
        Vertex V(float z) => new(new Vector3(0.05f, 0, z), Vector3.UnitX, Vector2.Zero, [new VertexBoneWeight(3, 1f)]);
        var model = new Model("view", [new Mesh([V(-0.1f), V(0.2f)], [new Submesh("stick", [new Triangle(0, 1, 1)])])], [new Material("stick")], skeleton);

        var held = HumanoidRetargeter.ExtractHeld(model, HumanoidBone.RightHand, new Vector3(0.09f, -0.02f, 0));

        // Slid under the knuckles, still along Z.
        var vertices = Assert.Single(held.Meshes).Vertices;
        AssertNear(new Vector3(0.09f, -0.02f, -0.1f), vertices[0].Position);
        AssertNear(new Vector3(0.09f, -0.02f, 0.2f), vertices[1].Position);
        AssertNear(new Vector3(0.04f, -0.02f, 0), held.Skeleton!.Bones.Single(bone => bone.Name == "Held").LocalBindPose.Translation);
    }

    [Fact]
    public void GarrysModHandsCoverEveryArmRole()
    {
        var roles = GarrysModHands.Skeleton.Bones.Select(bone => bone.Humanoid).ToHashSet();
        var expected = Enum.GetValues<HumanoidBone>().Where(role => role is not (HumanoidBone.LeftHeldItem or HumanoidBone.RightHeldItem));
        Assert.All(expected, role => Assert.Contains(role, roles));
        Assert.Equal("ValveBiped.Bip01_Spine4", GarrysModHands.Skeleton.Root!.Name);
        Assert.All(GarrysModHands.Skeleton.Bones, bone => Assert.True(bone.ParentIndex < bone.Index));
    }

    // Forward +X, left +Y, up +Z. Arms reach forward from shoulders above the chest; the right hand holds a knife.
    private static Model SourceModel(Quaternion leftSwing)
    {
        Bone B(int index, string name, int parent, float x, float y, float z, HumanoidBone? role) =>
            new(index, name, parent, new Transform(new Vector3(x, y, z), Quaternion.Identity, Vector3.One), role);
        var skeleton = new Skeleton(
        [
            B(0, "Chest", -1, 0, 0, 0.1f, HumanoidBone.Chest),
            B(1, "LClav", 0, 0, 0.1f, 0.1f, HumanoidBone.LeftClavicle),
            B(2, "LUpper", 1, 0, 0.1f, 0, HumanoidBone.LeftUpperArm),
            B(3, "LFore", 2, 0.2f, 0, 0, HumanoidBone.LeftForearm),
            B(4, "LHand", 3, 0.2f, 0, 0, HumanoidBone.LeftHand),
            B(5, "RClav", 0, 0, -0.1f, 0.1f, HumanoidBone.RightClavicle),
            B(6, "RUpper", 5, 0, -0.1f, 0, HumanoidBone.RightUpperArm),
            B(7, "RFore", 6, 0.2f, 0, 0, HumanoidBone.RightForearm),
            B(8, "RHand", 7, 0.2f, 0, 0, HumanoidBone.RightHand),
            B(9, "Held", 8, 0.05f, 0, 0, HumanoidBone.RightHeldItem),
            B(10, "Knife", 9, 0, 0, 0, null),
        ]);
        Vertex Weighted(Vector3 position, int bone) => new(position, Vector3.UnitZ, Vector2.Zero, [new VertexBoneWeight(bone, 1f)]);
        var mesh = new Mesh(
            [Weighted(new Vector3(0.45f, -0.2f, 0.2f), 10), Weighted(new Vector3(0, 0.2f, 0.2f), 2)],
            [new Submesh("knife", [new Triangle(0, 0, 0)]), new Submesh("arms", [new Triangle(1, 1, 1)])]);
        var clip = new AnimationClip("swing", 30f, 0f,
        [
            new BoneTransformTrack(2, [new TransformKeyframe(0f, skeleton.Bones[2].LocalBindPose with { Rotation = leftSwing })]),
        ]);
        return new Model("view", [mesh], [new Material("knife"), new Material("arms")], skeleton, [clip], FirstPerson: true);
    }

    // The same arms at twice the length, every bone twisted a quarter turn about its own length.
    private static Skeleton TargetSkeleton()
    {
        Bone B(int index, string name, int parent, Vector3 offset, Quaternion rotation, HumanoidBone role) =>
            new(index, name, parent, new Transform(offset, rotation, Vector3.One), role);
        var undo = Quaternion.Inverse(_twist);
        return new Skeleton(
        [
            B(0, "Chest", -1, new Vector3(0, 0, 0.2f), Quaternion.Identity, HumanoidBone.Chest),
            B(1, "L_Clavicle", 0, new Vector3(0, 0.2f, 0.2f), _twist, HumanoidBone.LeftClavicle),
            B(2, "L_UpperArm", 1, Vector3.Transform(new Vector3(0, 0.2f, 0), undo), Quaternion.Identity, HumanoidBone.LeftUpperArm),
            B(3, "L_Forearm", 2, Vector3.Transform(new Vector3(0.4f, 0, 0), undo), Quaternion.Identity, HumanoidBone.LeftForearm),
            B(4, "L_Hand", 3, Vector3.Transform(new Vector3(0.4f, 0, 0), undo), Quaternion.Identity, HumanoidBone.LeftHand),
            B(5, "R_Clavicle", 0, new Vector3(0, -0.2f, 0.2f), _twist, HumanoidBone.RightClavicle),
            B(6, "R_UpperArm", 5, Vector3.Transform(new Vector3(0, -0.2f, 0), undo), Quaternion.Identity, HumanoidBone.RightUpperArm),
            B(7, "R_Forearm", 6, Vector3.Transform(new Vector3(0.4f, 0, 0), undo), Quaternion.Identity, HumanoidBone.RightForearm),
            B(8, "R_Hand", 7, Vector3.Transform(new Vector3(0.4f, 0, 0), undo), Quaternion.Identity, HumanoidBone.RightHand),
        ]);
    }

    private static Matrix4x4[] Pose(Model model, AnimationClip clip)
    {
        var bones = model.Skeleton!.Bones;
        var tracks = clip.Tracks.OfType<BoneTransformTrack>().ToDictionary(track => track.BoneIndex);
        var world = new Matrix4x4[bones.Count];
        for (var index = 0; index < bones.Count; index++)
        {
            var transform = tracks.TryGetValue(index, out var track) ? track.Keyframes[0].Transform : bones[index].LocalBindPose;
            var local = Matrix4x4.CreateFromQuaternion(transform.Rotation) * Matrix4x4.CreateTranslation(transform.Translation);
            world[index] = bones[index].ParentIndex >= 0 ? local * world[bones[index].ParentIndex] : local;
        }

        return world;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"Expected {expected}, got {actual}.");
    }
}
