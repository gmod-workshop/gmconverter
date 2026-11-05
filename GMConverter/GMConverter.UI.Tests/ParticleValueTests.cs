using System.Numerics;
using GMConverter.SDK.Particles;

namespace GMConverter.UI.Tests;

public sealed class ParticleValueTests
{
    [Fact]
    public void CurvesInterpolateAndHoldPastTheirEnds()
    {
        var curve = ParticleFloat.Curve([new ParticleKey(0.25f, 2f), new ParticleKey(0.75f, 4f)]);

        Assert.Equal(2f, curve.Evaluate(0f));
        Assert.Equal(3f, curve.Evaluate(0.5f));
        Assert.Equal(4f, curve.Evaluate(1f));
        Assert.True(curve.IsAnimated);
        Assert.False(curve.IsRandom);
    }

    [Fact]
    public void RangesPickBetweenTheirCurves()
    {
        var range = ParticleFloat.Range(1f, 3f);

        Assert.Equal(1f, range.Evaluate(0.5f, random: 0f));
        Assert.Equal(2f, range.Evaluate(0.5f, random: 0.5f));
        Assert.Equal((1f, 3f), range.Bounds());
        Assert.True(range.IsRandom);
        Assert.False(range.IsAnimated);
    }

    [Fact]
    public void VectorsPickPerComponentAndSplitIntoFloats()
    {
        var box = ParticleVector.Range(Vector3.Zero, new Vector3(2f, 4f, 6f));

        Assert.Equal(new Vector3(1f, 0f, 6f), box.Evaluate(0f, new Vector3(0.5f, 0f, 1f)));
        Assert.Equal((0f, 4f), box.Component(1).Bounds());
    }
}
