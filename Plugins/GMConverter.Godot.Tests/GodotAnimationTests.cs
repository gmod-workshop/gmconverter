using System.Numerics;
using GMConverter.Godot.Animation;
using GMConverter.Godot.Resources;
using static GMConverter.Godot.Tests.GodotResourceBuilder;

namespace GMConverter.Godot.Tests;

public sealed class GodotAnimationTests
{
    [Fact]
    public void ReadsTransformAndValueTracks()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var animation = Read(
            ("length", Float(2f)),
            ("loop_mode", Int(1)),
            ("tracks/0/type", Text("rotation_3d")),
            ("tracks/0/path", NodePath(["Armature", "Skeleton3D"], ["Jaw"])),
            ("tracks/0/interp", Int(1)),
            ("tracks/0/keys", Float32s(0f, 1f, 0f, 0f, 0f, 1f, 1f, 1f, turn.X, turn.Y, turn.Z, turn.W)),
            ("tracks/1/type", Text("value")),
            ("tracks/1/path", NodePath(["Prop"], ["visible"])),
            ("tracks/1/interp", Int(1)),
            ("tracks/1/keys", Dictionary(
                (Text("times"), Float32s(0f, 0.5f)),
                (Text("transitions"), Float32s(1f, 1f)),
                (Text("update"), Int(1)),
                (Text("values"), Array(Bool(false), Bool(true))))),
            ("tracks/2/type", Text("method")),
            ("tracks/2/path", NodePath(["."], [])));

        Assert.Equal(2f, animation.Length);
        Assert.True(animation.Loops);
        Assert.Equal(2, animation.Tracks.Count); // the method track cannot move a model

        var rotation = animation.Tracks[0];
        Assert.Equal(GodotAnimationTrackKind.Rotation, rotation.Kind);
        Assert.Equal("Jaw", rotation.SubName);
        var halfway = Assert.IsType<Quaternion>(rotation.Sample(0.5f));
        Assert.Equal(MathF.PI / 4f, 2f * MathF.Acos(halfway.W), 4);
        Assert.Equal(turn, rotation.Sample(5f)); // past the last key, it holds

        var visible = animation.Tracks[1];
        Assert.Equal(false, visible.Sample(0.4f));
        Assert.Equal(true, visible.Sample(0.5f));
    }

    [Fact]
    public void EasesBetweenKeysByTheirTransition()
    {
        // A transition of 2 eases in: halfway through, the value has gone a quarter of the way.
        var animation = Read(
            ("tracks/0/type", Text("position_3d")),
            ("tracks/0/path", NodePath(["Lid"], [])),
            ("tracks/0/keys", Float32s(0f, 2f, 0f, 0f, 0f, 1f, 1f, 4f, 0f, 0f)));

        Assert.Equal(new Vector3(1f, 0f, 0f), animation.Tracks[0].Sample(0.5f));
    }

    [Fact]
    public void NearestInterpolationHoldsEachKey()
    {
        var animation = Read(
            ("tracks/0/type", Text("value")),
            ("tracks/0/path", NodePath(["Lid"], ["position"])),
            ("tracks/0/interp", Int(0)),
            ("tracks/0/keys", Dictionary(
                (Text("times"), Float32s(0f, 1f)),
                (Text("values"), Array(Vector(Vector3.Zero), Vector(Vector3.One))))));

        Assert.Equal(Vector3.Zero, animation.Tracks[0].Sample(0.9f));
        Assert.Equal(Vector3.One, animation.Tracks[0].Sample(1f));
    }

    private static GodotAnimation Read(params (string Name, Action<BinaryWriter> Write)[] properties)
    {
        var builder = new GodotResourceBuilder();
        builder.Add("Animation_1", "Animation", properties);
        return GodotAnimation.Read(GodotResourceFile.Read(builder.Build("Animation")).Main, "test");
    }
}
