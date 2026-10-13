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
}
