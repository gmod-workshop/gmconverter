namespace GMConverter.UnrealEngine.Explorer;

internal sealed record ExportSentinel(
    int Version,
    string Fingerprint,
    string ArchivePath,
    string ArchiveEntryPath,
    string ManifestRelativePath,
    string? AnimationRelativePath,
    string? Details,
    long TotalBytes,
    DateTimeOffset WrittenUtc,
    DateTimeOffset LastUsedUtc);
