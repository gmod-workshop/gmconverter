using System.Numerics;
using GMConverter.Unity.Animation;

namespace GMConverter.Unity.Tests;

public sealed class UnityAnimationTests
{
    [Fact]
    public void PathHashMatchesUnityCrc()
    {
        // From Liar's Bar's Toar avatar, whose m_TOS maps this hash to the pelvis.
        Assert.Equal(883898639u, UnityPathHash.Compute("Base HumanPelvis"));
        Assert.Equal(0u, UnityPathHash.Compute(string.Empty));

        // The attribute of Liar's Bar's GameObject on/off curves.
        Assert.Equal(2086281974u, UnityPathHash.Compute("m_IsActive"));
    }

    [Fact]
    public void SwingTwistTwistsAboutXAndSwingsAboutYz()
    {
        AssertRotates(UnityAvatarAxes.SwingTwist(Vector3.Zero), Vector3.UnitY, Vector3.UnitY);
        AssertRotates(UnityAvatarAxes.SwingTwist(new Vector3(MathF.PI / 2f, 0f, 0f)), Vector3.UnitY, Vector3.UnitZ);
        AssertRotates(UnityAvatarAxes.SwingTwist(new Vector3(0f, 0f, MathF.PI / 2f)), Vector3.UnitX, Vector3.UnitY);
        AssertRotates(UnityAvatarAxes.SwingTwist(new Vector3(0f, MathF.PI / 2f, 0f)), Vector3.UnitX, -Vector3.UnitZ);
    }

    [Fact]
    public void SwingOnBothAxesUsesHalfAngleTangents()
    {
        // 90° on y and z: the axis is (0, 1, 1) and the angle 2·atan(√2), not √2·90°.
        var swing = UnityAvatarAxes.SwingTwist(new Vector3(0f, MathF.PI / 2f, MathF.PI / 2f));
        var angle = 2f * MathF.Acos(Math.Clamp(swing.W, -1f, 1f));

        Assert.Equal(2f * MathF.Atan(MathF.Sqrt(2f)), angle, 4);
        Assert.Equal(swing.Y, swing.Z, 5);
    }

    [Fact]
    public void MusclesReachTheLimitOnTheirSide()
    {
        var axes = new UnityAvatarAxes(Quaternion.Identity, Quaternion.Identity, Vector3.One, new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 1.5f));

        AssertRotates(axes.Rotate(new Vector3(0f, 0f, 1f)), Vector3.UnitX, new Vector3(MathF.Cos(1.5f), MathF.Sin(1.5f), 0f));
        AssertRotates(axes.Rotate(new Vector3(0f, 0f, -1f)), Vector3.UnitX, new Vector3(MathF.Cos(-0.5f), MathF.Sin(-0.5f), 0f));
    }

    [Fact]
    public void SignFlipsAMuscleAndFramesWrapIt()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var axes = new UnityAvatarAxes(turn, turn, new Vector3(1f, 1f, -1f), new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f));

        // PreQ * SwingTwist * PostQ⁻¹ with zero muscles is PreQ * PostQ⁻¹, here no rotation at all.
        AssertRotates(axes.Rotate(Vector3.Zero), Vector3.UnitX, Vector3.UnitX);
        Assert.True(Quaternion.Dot(axes.Rotate(new Vector3(0f, 0f, 0.5f)), axes.Rotate(new Vector3(0f, 0f, -0.5f))) < 0.99f);
    }

    [Fact]
    public void TwistFollowsTheMuscleAndTurnsAboutTheTwistAxis()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var axes = new UnityAvatarAxes(Quaternion.Identity, turn, new Vector3(-1f, 1f, 1f), new Vector3(-0.5f, 0f, 0f), new Vector3(1f, 0f, 0f));

        Assert.Equal(-1f, axes.Twist(1f), 5);
        Assert.Equal(0.5f, axes.Twist(-1f), 5);

        // PostQ turns the muscle frame's x onto the bone's y, so the twist turns about y.
        AssertRotates(axes.TwistBy(MathF.PI / 2f), Vector3.UnitY, Vector3.UnitY);
        AssertRotates(axes.TwistBy(MathF.PI / 2f), Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f)));
    }

    [Fact]
    public void XformUnderComposesLikeUnityParenting()
    {
        var parent = new UnityXform(new Vector3(1f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f), new Vector3(2f, 2f, 2f));
        var child = new UnityXform(new Vector3(0f, 0f, 1f), Quaternion.Identity, Vector3.One);

        var global = child.Under(parent);

        // Scaled to 2, turned 90° about Y (Z goes to X), then moved.
        Assert.Equal(3f, global.Translation.X, 4);
        Assert.Equal(0f, global.Translation.Z, 4);
        Assert.Equal(new Vector3(2f, 2f, 2f), global.Scale);
    }

    private static void AssertRotates(Quaternion rotation, Vector3 from, Vector3 expected)
    {
        var actual = Vector3.Transform(from, rotation);
        Assert.True(Vector3.Distance(actual, expected) < 1e-4f, $"Expected {expected}, got {actual}.");
    }
}
