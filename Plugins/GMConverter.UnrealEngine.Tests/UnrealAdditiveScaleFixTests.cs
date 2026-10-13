using CUE4Parse.UE4.Objects.Core.Math;
using GMConverter.UnrealEngine.Archives;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UnrealAdditiveScaleFixTests
{
    [Fact]
    public void OnlyBonesDoubledOnEveryFrameGetTheBaseScale()
    {
        var baseScale = new FVector(1, 1, 1);
        FVector[] doubled = [new(2, 2, 2), new(2, 2, 2)];
        FVector[] animated = [new(2, 2, 2), new(1.5f, 1.5f, 1.5f)];
        FVector[] untouched = [new(1, 1, 1)];
        FVector[] empty = [];

        var fixedBones = UnrealAdditiveScaleFix.RestoreDoubledScales([doubled, animated, untouched, empty], [baseScale, baseScale, baseScale, baseScale]);

        Assert.Equal(1, fixedBones);
        Assert.All(doubled, key => Assert.Equal(baseScale, key));
        Assert.Equal(new FVector(2, 2, 2), animated[0]);
    }
}
