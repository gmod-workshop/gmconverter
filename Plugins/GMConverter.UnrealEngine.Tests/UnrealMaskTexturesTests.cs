using GMConverter.Core.Textures;
using GMConverter.UnrealEngine.Materials;
using GMConverter.UnrealEngine.Profiles.ZeroCompany;

namespace GMConverter.UnrealEngine.Tests;

public sealed class UnrealMaskTexturesTests
{
    [Fact]
    public void MetallicRoughnessMasksAreRepackedAsSpecularMasks()
    {
        var factory = new DefaultTextureFactory();
        // A metal, rough texel and a non-metal, smooth one.
        var mask = factory.FromRgba("T_Coil_MSK", 2, 1, [255, 200, 0, 0, 0, 30, 0, 0]);

        var repacked = UnrealMaskTextures.ToSpecularMasks(mask, factory).GetRgbaPixels();

        Assert.Equal([128, 255, 200, 255, 128, 0, 30, 255], repacked);
    }

    [Fact]
    public void ZeroCompanyNamesItsBaseTexturesAndMaskPacking()
    {
        var conventions = new ZeroCompanyProfile().MaterialConventions;

        Assert.Equal(UnrealMaskPacking.MetallicRoughness, conventions.MaskPacking);
        Assert.Contains(conventions.ChannelAliases, alias => alias.Channel == "Diffuse" && alias.Aliases[0] == "Base BSC");
        Assert.Contains(conventions.ChannelAliases, alias => alias.Channel == "SpecularMasks" && alias.Aliases.Contains("Base Mask 1 VT"));
    }
}
