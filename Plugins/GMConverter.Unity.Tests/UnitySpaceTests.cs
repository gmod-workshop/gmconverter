using System.Numerics;
using GMConverter.Unity.Geometry;

namespace GMConverter.Unity.Tests;

public sealed class UnitySpaceTests
{
    [Fact]
    public void PositionMapsUnityAxesToSdkAxes()
    {
        // Unity forward (+Z) becomes +X, right (+X) becomes -Y (the SDK's Y points left), up stays up.
        Assert.Equal(Vector3.UnitX, UnitySpace.Position(Vector3.UnitZ));
        Assert.Equal(-Vector3.UnitY, UnitySpace.Position(Vector3.UnitX));
        Assert.Equal(Vector3.UnitZ, UnitySpace.Position(Vector3.UnitY));
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 90f)]
    [InlineData(1f, 0f, 0f, -35f)]
    [InlineData(0.3f, -0.5f, 0.8f, 120f)]
    public void RotationRotatesConvertedVectorsLikeUnity(float axisX, float axisY, float axisZ, float degrees)
    {
        var unityRotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(axisX, axisY, axisZ)), float.DegreesToRadians(degrees));
        var point = new Vector3(0.25f, -1.5f, 2f);

        var rotatedThenConverted = UnitySpace.Position(Vector3.Transform(point, unityRotation));
        var convertedThenRotated = Vector3.Transform(UnitySpace.Position(point), UnitySpace.Rotation(unityRotation));

        Assert.True(Vector3.Distance(rotatedThenConverted, convertedThenRotated) < 1e-5f);
    }

    [Fact]
    public void RotationTurnsUnityClockwiseYawIntoSdkClockwiseYaw()
    {
        // Unity's positive yaw turns forward toward the right; so must the converted rotation.
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.DegreesToRadians(90f));
        var forward = Vector3.Transform(UnitySpace.Forward, UnitySpace.Rotation(yaw));

        Assert.True(Vector3.Distance(-Vector3.UnitY, forward) < 1e-5f);
    }
}
