using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using GMConverter.UnrealEngine.Archives;

namespace GMConverter.UnrealEngine.Tests;

public sealed class Cue4ParseImageDecoderTests
{
    // Zero Company's metal/roughness masks keep their data in RGB with an alpha of zero; a
    // premultiplying conversion turns them black.
    [Fact]
    public void ColourUnderZeroAlphaSurvives()
    {
        var bgra = new CTexture(2, 1, EPixelFormat.PF_B8G8R8A8, [0, 88, 225, 0, 10, 20, 30, 255]);

        var decoded = Cue4ParseImageDecoder.TryCopyRgba(bgra)!;

        Assert.Equal([225, 88, 0, 0, 30, 20, 10, 255], decoded.Pixels);
    }

    [Fact]
    public void GreyscaleTexturesExpandToOpaqueRgba()
    {
        var decoded = Cue4ParseImageDecoder.TryCopyRgba(new CTexture(1, 1, EPixelFormat.PF_G8, [77]))!;

        Assert.Equal([77, 77, 77, 255], decoded.Pixels);
    }

    [Fact]
    public void OtherFormatsAreLeftToSkiaSharp()
    {
        Assert.Null(Cue4ParseImageDecoder.TryCopyRgba(new CTexture(1, 1, EPixelFormat.PF_FloatRGBA, new byte[8])));
    }
}
