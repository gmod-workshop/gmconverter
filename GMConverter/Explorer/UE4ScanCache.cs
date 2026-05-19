using System.Security.Cryptography;
using System.Text;
using GMConverter.Common;
using MessagePack;
using MessagePack.Resolvers;

namespace GMConverter.Explorer;

// Persistent on-disk cache for UE4Explorer.Scan results. The first scan of a Fortnite-sized
// archive set takes ~30 seconds (most of which is CUE4Parse mounting AES-encrypted containers
// and parsing AssetRegistry.bin). The output — an ExplorerFileEntry[] — is purely derived from
// the on-disk PAK/utoc/ucas set, so we can hash that set as a fingerprint, persist the entry
// list, and skip both the mount and the registry parse on subsequent scans of the same install.
//
// Storage format: MessagePack binary via the contractless resolver. Replaces the original
// System.Text.Json format because deserializing 138K Fortnite entries from JSON was the largest
// single cost (~1.1s) in an otherwise sub-100 ms hot-path scan. MessagePack's length-prefixed
// binary representation is ~10x faster to parse and produces roughly half the on-disk size.
internal static class UE4ScanCache
{
    // Bump this when ExplorerFileEntry or the cached envelope changes shape in a non-additive way
    // — readers will treat older files as a miss and regenerate.
    private const int _cacheVersion = 2;

    // Files whose presence/size/mtime defines "this archive set." We deliberately do not hash file
    // contents (would be slower than the scan we're trying to skip). The set covers PAK and IoStore
    // archives plus their signature/manifest sidecars — anything that changes when the game updates.
    private static readonly string[] _fingerprintExtensions =
    [
        ".pak",
        ".utoc",
        ".ucas",
        ".sig"
    ];

    private static readonly MessagePackSerializerOptions _serializerOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    public static string CacheDirectory { get; } = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GMConverter",
        "cache",
        "ue4-scan");

    public static ArchiveFingerprint ComputeFingerprint(string archiveDirectory)
    {
        using var scope = PerfTimer.Measure("ue4.scan-cache", "ComputeFingerprint", archiveDirectory);

        var fullPath = Path.GetFullPath(archiveDirectory);
        if (!Directory.Exists(fullPath))
        {
            return new ArchiveFingerprint(string.Empty, 0, 0);
        }

        List<(string Relative, long Length, long Ticks)> files = [];
        long totalBytes = 0;

        foreach (var file in EnumerateFingerprintFiles(fullPath))
        {
            FileInfo info;
            try
            {
                info = new FileInfo(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (!info.Exists)
            {
                continue;
            }

            var relative = Path.GetRelativePath(fullPath, info.FullName).Replace('\\', '/');
            files.Add((relative, info.Length, info.LastWriteTimeUtc.Ticks));
            totalBytes += info.Length;
        }

        files.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Relative, b.Relative));

        var builder = new StringBuilder();
        builder.Append(fullPath.Replace('\\', '/'));
        builder.Append('\n');
        foreach (var (relative, length, ticks) in files)
        {
            builder.Append(relative);
            builder.Append('|');
            builder.Append(length);
            builder.Append('|');
            builder.Append(ticks);
            builder.Append('\n');
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16].ToLowerInvariant();
        PerfTimer.Log("ue4.scan-cache",
            $"fingerprint files={files.Count} totalBytes={totalBytes} hash={hash}");
        return new ArchiveFingerprint(hash, files.Count, totalBytes);
    }

    public static bool TryRead(ArchiveFingerprint fingerprint, out IReadOnlyList<ExplorerFileEntry> entries)
    {
        entries = [];
        if (string.IsNullOrEmpty(fingerprint.Hash))
        {
            return false;
        }

        var cachePath = GetCachePath(fingerprint);
        if (!File.Exists(cachePath))
        {
            return false;
        }

        using var scope = PerfTimer.Measure("ue4.scan-cache", "TryRead", cachePath);
        try
        {
            using var stream = File.OpenRead(cachePath);
            var envelope = MessagePackSerializer.Deserialize<CachedScanEnvelope>(stream, _serializerOptions);
            if (envelope is null ||
                envelope.Version != _cacheVersion ||
                envelope.Fingerprint != fingerprint.Hash ||
                envelope.Entries is null)
            {
                return false;
            }

            entries = envelope.Entries;
            PerfTimer.Log("ue4.scan-cache", $"cache HIT entries={entries.Count}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MessagePackSerializationException or InvalidOperationException)
        {
            // Any expected read failure (corrupt file, schema mismatch, IO error, MessagePack
            // runtime issue, invalid stream state) falls back to a live scan. The cache is purely
            // opportunistic and the worst outcome on a miss is paying the original 30s scan cost
            // again. Anything outside this set (OOM, programmer error) propagates so a real bug
            // doesn't get swallowed and disguised as a cache miss.
            PerfTimer.Log("ue4.scan-cache", $"TryRead failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static void Write(
        ArchiveFingerprint fingerprint,
        string archivePath,
        IReadOnlyList<ExplorerFileEntry> entries)
    {
        if (string.IsNullOrEmpty(fingerprint.Hash))
        {
            return;
        }

        using var scope = PerfTimer.Measure(
            "ue4.scan-cache",
            "Write",
            $"entries={entries.Count} hash={fingerprint.Hash}");

        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var cachePath = GetCachePath(fingerprint);
            tempPath = cachePath + ".tmp";
            var envelope = new CachedScanEnvelope(
                _cacheVersion,
                fingerprint.Hash,
                archivePath,
                DateTimeOffset.UtcNow,
                fingerprint.FileCount,
                fingerprint.TotalBytes,
                entries);

            using (var stream = File.Create(tempPath))
            {
                MessagePackSerializer.Serialize(stream, envelope, _serializerOptions);
            }
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MessagePackSerializationException)
        {
            // Cache write is best-effort across the three failure modes we actually expect:
            // filesystem errors, permission errors, and MessagePack contract mismatches. Any other
            // exception (OOM, programmer error) should propagate so it doesn't get silently lost
            // — the live scan result is still in memory and the user has already seen the scan
            // succeed at this point, so the only consumer affected is the cache write.
            PerfTimer.Log("ue4.scan-cache", $"Write failed: {ex.GetType().Name}: {ex.Message}");
            if (tempPath is not null)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                    // Leaving an orphan .tmp behind is benign — the next Write overwrites it.
                }
            }
        }
    }

    public static void Clear()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            return;
        }
        try
        {
            Directory.Delete(CacheDirectory, recursive: true);
        }
        catch (IOException ex)
        {
            PerfTimer.Log("ue4.scan-cache", $"Clear failed (IO) {CacheDirectory}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            PerfTimer.Log("ue4.scan-cache", $"Clear failed (access) {CacheDirectory}: {ex.Message}");
        }
    }

    private static IEnumerable<string> EnumerateFingerprintFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return FilterFingerprintFiles(files);
    }

    private static IEnumerable<string> FilterFingerprintFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (Array.Exists(_fingerprintExtensions, ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase)))
            {
                yield return file;
                continue;
            }

            var name = Path.GetFileName(file);
            if (name.StartsWith("Manifest_", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static string GetCachePath(ArchiveFingerprint fingerprint)
    {
        // fingerprint.Hash is a 16-char lowercase hex SHA-256 prefix we computed ourselves, so
        // it's never rooted in practice — but routing through Path.GetFileName keeps the call
        // analyzer-clean and defends against a future refactor that lets non-hex content into the
        // Hash field.
        var fileName = Path.GetFileName($"{fingerprint.Hash}.msgpack");
        return Path.Join(CacheDirectory, fileName);
    }
}

internal sealed record ArchiveFingerprint(string Hash, int FileCount, long TotalBytes);

[MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
internal sealed record CachedScanEnvelope(
    int Version,
    string Fingerprint,
    string ArchivePath,
    DateTimeOffset ScannedAt,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<ExplorerFileEntry> Entries);
