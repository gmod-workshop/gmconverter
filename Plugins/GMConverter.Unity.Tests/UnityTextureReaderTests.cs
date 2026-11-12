using GMConverter.Unity.Textures;

namespace GMConverter.Unity.Tests;

public sealed class UnityTextureReaderTests
{
    [Theory]
    [InlineData("2022.3.27f1", true)]
    [InlineData("2017.3.0f3", true)]
    [InlineData("2017.2.1f1", false)]
    [InlineData("5.6.7f1", false)]
    public void UnityCrunchStartsWithUnity2017Point3(string version, bool expected)
    {
        Assert.Equal(expected, UnityTextureReader.UsesUnityCrunch(version));
    }
}
