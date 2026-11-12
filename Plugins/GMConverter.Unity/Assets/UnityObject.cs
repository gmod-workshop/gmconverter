using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace GMConverter.Unity.Assets;

/// <summary>One deserialized object of a serialized file, with the file it was read from.</summary>
internal sealed record UnityObject(AssetsFileInstance File, AssetFileInfo Info, AssetTypeValueField Fields)
{
    public long PathId => Info.PathId;

    public AssetClassID ClassId => (AssetClassID)Info.TypeId;

    public string FileName => File.name;

    public string Name => Fields["m_Name"] is { IsDummy: false } name ? name.AsString : string.Empty;

    public AssetTypeValueField this[string name] => Fields[name];

    /// <summary>A key unique across the files of one build.</summary>
    public UnityObjectKey Key => new(FileName, PathId);
}
