using GMConverter.SDK.Assets;
using GMConverter.SDK.Audio;
using GMConverter.SDK.Options;
using GMConverter.Unity.Importers;

namespace GMConverter.Unity.Tests;

public sealed class FsbImporterTests
{
    [Fact]
    public void ImporterReadsSoundBanksWithAVgmstreamOverride()
    {
        var importer = new FsbImporter();

        Assert.Equal("fsb", importer.Format);
        Assert.Equal(AssetKind.Sound, ((GMConverter.SDK.Importers.IImporter)importer).Kind);
        Assert.Equal([".fsb"], importer.FileExtensions);
        Assert.Equal(OptionType.Path, importer.OptionSchema.Find(VgmstreamDecoder.PathOption)?.Type);
    }
}
