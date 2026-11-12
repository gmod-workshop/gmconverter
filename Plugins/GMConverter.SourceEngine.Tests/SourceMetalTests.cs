using GMConverter.Core.Textures;
using GMConverter.SourceEngine.Materials;

namespace GMConverter.SourceEngine.Tests;

public sealed class SourceMetalTests
{
    [Fact]
    public void MetalIsDarkenedAndPaintIsKept()
    {
        var factory = new DefaultTextureFactory();
        var albedo = factory.FromRgba("albedo", 2, 1, [200, 200, 200, 255, 200, 100, 50, 255]);
        // SpecularMasks: the first texel is fully metallic (G), the second not at all.
        var masks = factory.FromRgba("masks", 2, 1, [128, 255, 0, 255, 128, 0, 0, 255]);

        var darkened = albedo.WithMetalDarkened(masks, factory).GetRgbaPixels();

        Assert.Equal([100, 100, 100, 255, 200, 100, 50, 255], darkened);
    }

    [Fact]
    public void WithoutMetalTheTextureIsUnchanged()
    {
        var factory = new DefaultTextureFactory();
        var albedo = factory.FromRgba("albedo", 1, 1, [200, 100, 50, 255]);

        Assert.Same(albedo, albedo.WithMetalDarkened(factory.FromRgba("masks", 1, 1, [128, 0, 0, 255]), factory));
    }

    [Fact]
    public void PhongExponentKeepsMetalnessInGreenForAlbedoTint()
    {
        var factory = new DefaultTextureFactory();
        var masks = factory.FromRgba("masks", 1, 1, [128, 255, 55, 255]);

        Assert.Equal([200, 255, 200, 128], masks.ToSourcePhongExponent(factory).GetRgbaPixels());
    }

    [Fact]
    public void PhongMaskKeepsAQuarterForPaintAndAllForSmoothMetal()
    {
        var factory = new DefaultTextureFactory();
        // Smooth metal, rough metal, smooth paint, rough paint.
        var masks = factory.FromRgba("masks", 4, 1, [128, 255, 0, 255, 128, 255, 255, 255, 128, 0, 0, 255, 128, 0, 255, 255]);

        var mask = masks.ToSourcePhongMask(factory).GetRgbaPixels();

        Assert.Equal([255, 102, 64, 26], new[] { mask[0], mask[4], mask[8], mask[12] });
    }

    [Fact]
    public void MetalFlattensTheFresnelAndRaisesTheBoost()
    {
        Assert.Equal(new SourcePhongSettings("1", "30", "[0.1 0.4 1]", "[0.05 0.05 0.05]"), SourcePhongSettings.For(metalness: 0f, gloss: 0.5f));
        Assert.Equal(new SourcePhongSettings("2", "50", "[0.6 0.8 1]", "[0.4 0.4 0.4]"), SourcePhongSettings.For(metalness: 1f, gloss: 1f));
    }

    [Fact]
    public void AveragesComeFromMetalAndRoughness()
    {
        var factory = new DefaultTextureFactory();
        var masks = factory.FromRgba("masks", 1, 1, [128, 255, 51, 255]);

        Assert.Equal((1f, 0.8f), masks.AverageMetalAndGloss());
    }
}
