using MessagePack;

namespace GMConverter.Explorer;

// MessagePackObject(true) → keyAsPropertyName mode, which makes MessagePack-CSharp's contractless
// path work with records that have a primary constructor. Without an explicit attribute, the
// runtime contractless resolver can't reliably round-trip a record with positional parameters
// (it leaves the .msgpack.tmp file empty and the serialize call throws).
[MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
internal sealed record ExplorerFileEntry(
    string DisplayPath,
    string FilePath,
    string InputFormat,
    string MaterialDirectory,
    string SearchRoot,
    string? ArchivePath = null,
    string? ArchiveEntryPath = null,
    string? ExplorerId = null,
    string? Details = null,
    bool IsConvertible = true,
    string? AssetClass = null);
