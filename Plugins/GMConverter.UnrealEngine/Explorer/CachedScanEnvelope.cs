using GMConverter.SDK.Explorer;
using MessagePack;

namespace GMConverter.UnrealEngine.Explorer;

[MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
internal sealed record CachedScanEnvelope(
    int Version,
    string Fingerprint,
    string ArchivePath,
    DateTimeOffset ScannedAt,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<ExplorerFileEntry> Entries);
