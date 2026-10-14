using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Tests;

public sealed class UnityAssetReferenceTests
{
    [Fact]
    public void ObjectKeyRoundTripsThroughText()
    {
        var key = new UnityObjectKey("sharedassets2.assets", -4_611_686_018_427_387_904);

        Assert.Equal(key, UnityObjectKey.Parse(key.ToString()));
    }

    [Theory]
    [InlineData("level5")]
    [InlineData("level5:")]
    [InlineData(":12")]
    public void ObjectKeyRejectsMalformedText(string text)
    {
        Assert.Throws<FormatException>(() => UnityObjectKey.Parse(text));
    }

    [Fact]
    public void ReferenceRoundTripsThroughFile()
    {
        var path = Path.Join(Path.GetTempPath(), $"gmc-unity-{Guid.NewGuid():N}", "Mesh.unityasset");
        try
        {
            var reference = new UnityAssetReference(@"C:\Games\Liar's Bar\Liar's Bar_Data", "sharedassets4.assets:1234", "level5:99");
            reference.Write(path);

            var read = UnityAssetReference.Read(path);

            Assert.Equal(reference, read);
            Assert.Equal(new UnityObjectKey("sharedassets4.assets", 1234), read.ObjectKey);
            Assert.Equal(new UnityObjectKey("level5", 99), read.MaterialRendererKey);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
