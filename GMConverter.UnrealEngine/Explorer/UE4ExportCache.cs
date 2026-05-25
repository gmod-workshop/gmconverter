using System.Text.Json;
using GMConverter.SDK.Common;
using GMConverter.SDK.Explorer;

namespace GMConverter.UnrealEngine.Explorer;

// Persistent per-asset cache for the heavy work inside UE4Explorer.ResolveEntry: CUE4Parse mesh
// extraction, texture decode, material sidecar export, and multi-layer baking. The cache lives
// alongside the existing `__ue4_exports/<asset-hash>/` directory that ResetExportRoot already
// manages, tagged with a sentinel JSON file whose contents identify the archive fingerprint and
// tool version that produced the tree. On a re-resolve, a matching sentinel lets us skip the
// minutes-long extraction and return the previously written `.ue4scene` manifest directly. A
// mismatch (game updated, exporter logic bumped) forces a rebuild.
internal static class UE4ExportCache
{
    // Bump when ResolveEntry's on-disk layout or the export sidecar shape changes in a way that
    // makes existing caches stale. Readers treat any mismatch as a miss and let the caller rebuild.
    private const int _toolVersion = 1;
    private const string _sentinelFileName = "__export.cache.json";

    // Default per-archive cache cap. Conservative — Fortnite assets typically decode to 5-50 MB
    // per mesh once textures are included, so the cap covers roughly 100-1000 cached previews
    // before LRU eviction kicks in. Configurable via env var for power users who want more.
    private const long _defaultCacheCapBytes = 5L * 1024 * 1024 * 1024;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = false
    };

    public static bool TryRead(
        string exportRoot,
        string archiveFingerprint,
        out ExplorerResolvedEntry resolved)
    {
        resolved = null!;
        if (string.IsNullOrEmpty(archiveFingerprint) || !Directory.Exists(exportRoot))
        {
            return false;
        }

        // Path.Join over Path.Combine so a rooted sentinel name (impossible here since it's a
        // const, but defensive against future refactors) can't silently replace exportRoot.
        var sentinelPath = Path.Join(exportRoot, _sentinelFileName);
        if (!File.Exists(sentinelPath))
        {
            return false;
        }

        using var scope = PerfTimer.Measure("ue4.export-cache", "TryRead", exportRoot);
        ExportSentinel? sentinel;
        try
        {
            using var stream = File.OpenRead(sentinelPath);
            sentinel = JsonSerializer.Deserialize<ExportSentinel>(stream, _jsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        if (sentinel is null ||
            sentinel.Version != _toolVersion ||
            sentinel.Fingerprint != archiveFingerprint ||
            string.IsNullOrEmpty(sentinel.ManifestRelativePath))
        {
            return false;
        }

        // Sentinel data comes from a JSON file we wrote, but a tampered or corrupted cache could
        // contain a rooted path that would let Path.Combine silently drop exportRoot and let
        // File.Exists probe arbitrary filesystem locations. PathHelpers.TryResolveUnderRoot
        // rejects rooted relatives and verifies the resolved full path is still inside
        // exportRoot before we trust it as a manifest reference.
        if (!PathHelpers.TryResolveUnderRoot(exportRoot, sentinel.ManifestRelativePath, out var manifestPath) ||
            !File.Exists(manifestPath))
        {
            return false;
        }

        // Best-effort: refresh the LastUsedUtc so a hit promotes the entry in the LRU. A write
        // failure here just means the entry ages from its original timestamp — not a correctness
        // issue, so we swallow.
        try
        {
            sentinel = sentinel with { LastUsedUtc = DateTimeOffset.UtcNow };
            File.WriteAllText(sentinelPath, JsonSerializer.Serialize(sentinel, _jsonOptions));
        }
        catch (IOException ex)
        {
            PerfTimer.Log("ue4.export-cache", $"LastUsedUtc refresh skipped (IO): {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            PerfTimer.Log("ue4.export-cache", $"LastUsedUtc refresh skipped (access): {ex.Message}");
        }

        // AnimationRelativePath gets the same rooted-and-escape validation as the manifest path.
        // A failed resolution (rooted, traversal-attempt, or just absent) becomes a null
        // AnimationPath on the resolved entry, which the downstream pipeline already handles.
        string? animationPath = null;
        if (!string.IsNullOrEmpty(sentinel.AnimationRelativePath) &&
            PathHelpers.TryResolveUnderRoot(exportRoot, sentinel.AnimationRelativePath, out var resolvedAnimationPath))
        {
            animationPath = resolvedAnimationPath;
        }

        resolved = new ExplorerResolvedEntry(
            manifestPath,
            exportRoot,
            AnimationPath: animationPath,
            Details: sentinel.Details);

        PerfTimer.Log("ue4.export-cache", $"cache HIT manifest={manifestPath}");
        return true;
    }

    public static void Write(
        string exportRoot,
        string archiveFingerprint,
        string archivePath,
        string archiveEntryPath,
        ExplorerResolvedEntry resolved)
    {
        if (string.IsNullOrEmpty(archiveFingerprint) || !Directory.Exists(exportRoot))
        {
            return;
        }

        using var scope = PerfTimer.Measure("ue4.export-cache", "Write", exportRoot);

        long totalBytes = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(exportRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    totalBytes += new FileInfo(file).Length;
                }
                catch (IOException ex)
                {
                    // Best-effort size accounting: a transient FileInfo read failure on a single
                    // entry just leaves it out of totalBytes. Logged so it's diagnosable but never
                    // blocks the cache write.
                    PerfTimer.Log("ue4.export-cache", $"size probe skipped {file}: {ex.Message}");
                }
            }
        }
        catch (IOException ex)
        {
            // Outer enumerate-files failure (e.g. directory disappeared mid-walk). The cache
            // write below still proceeds with the partial totalBytes, which is fine for the LRU
            // sweeper's purposes.
            PerfTimer.Log("ue4.export-cache", $"size walk failed: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        var manifestRelative = Path.GetRelativePath(exportRoot, resolved.InputPath);
        var animationRelative = string.IsNullOrEmpty(resolved.AnimationPath)
            ? null
            : Path.GetRelativePath(exportRoot, resolved.AnimationPath);
        var sentinel = new ExportSentinel(
            _toolVersion,
            archiveFingerprint,
            archivePath,
            archiveEntryPath,
            manifestRelative,
            animationRelative,
            resolved.Details,
            totalBytes,
            now,
            now);

        var sentinelPath = Path.Join(exportRoot, _sentinelFileName);
        var tempPath = sentinelPath + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, sentinel, _jsonOptions);
            }
            File.Move(tempPath, sentinelPath, overwrite: true);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        SweepLru(exportRoot);
    }

    public static void Clear(string archiveExportsRoot)
    {
        if (!Directory.Exists(archiveExportsRoot))
        {
            return;
        }
        try
        {
            Directory.Delete(archiveExportsRoot, recursive: true);
        }
        catch (IOException ex)
        {
            PerfTimer.Log("ue4.export-cache", $"Clear failed (IO) {archiveExportsRoot}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            PerfTimer.Log("ue4.export-cache", $"Clear failed (access) {archiveExportsRoot}: {ex.Message}");
        }
    }

    // Walk all sibling cache directories under the same archive, total their reported sizes from
    // sentinel files, and evict oldest-LastUsedUtc directories until the total is back under the
    // cap. Directories without a sentinel are skipped — they belong to an in-flight resolve or
    // a previous tool version and will be wiped by ResetExportRoot the next time their key is
    // requested.
    private static void SweepLru(string justWrittenExportRoot)
    {
        var capBytes = ResolveCapBytes();
        if (capBytes <= 0)
        {
            return;
        }

        var archiveExportsRoot = Path.GetDirectoryName(justWrittenExportRoot);
        if (string.IsNullOrEmpty(archiveExportsRoot) || !Directory.Exists(archiveExportsRoot))
        {
            return;
        }

        List<(string Directory, ExportSentinel Sentinel)> sentinels = [];
        long totalBytes = 0;
        foreach (var directory in Directory.EnumerateDirectories(archiveExportsRoot))
        {
            var sentinelPath = Path.Join(directory, _sentinelFileName);
            if (!File.Exists(sentinelPath))
            {
                continue;
            }

            ExportSentinel? sentinel;
            try
            {
                using var stream = File.OpenRead(sentinelPath);
                sentinel = JsonSerializer.Deserialize<ExportSentinel>(stream, _jsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            if (sentinel is null)
            {
                continue;
            }

            sentinels.Add((directory, sentinel));
            totalBytes += sentinel.TotalBytes;
        }

        if (totalBytes <= capBytes)
        {
            return;
        }

        sentinels.Sort(static (a, b) => a.Sentinel.LastUsedUtc.CompareTo(b.Sentinel.LastUsedUtc));
        var fullJustWritten = Path.GetFullPath(justWrittenExportRoot);
        foreach (var (directory, sentinel) in sentinels)
        {
            if (totalBytes <= capBytes)
            {
                break;
            }

            // Never evict the entry we just wrote; we'd undo the work we did to populate it.
            if (string.Equals(Path.GetFullPath(directory), fullJustWritten, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                totalBytes -= sentinel.TotalBytes;
                PerfTimer.Log(
                    "ue4.export-cache",
                    $"LRU evict {Path.GetFileName(directory)} bytes={sentinel.TotalBytes}");
            }
            catch (IOException ex)
            {
                PerfTimer.Log("ue4.export-cache", $"LRU evict failed (IO) {Path.GetFileName(directory)}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                PerfTimer.Log("ue4.export-cache", $"LRU evict failed (access) {Path.GetFileName(directory)}: {ex.Message}");
            }
        }
    }

    private static long ResolveCapBytes()
    {
        var raw = Environment.GetEnvironmentVariable("GMCONVERTER_EXPORT_CACHE_BYTES");
        if (!string.IsNullOrEmpty(raw) &&
            long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            return parsed;
        }
        return _defaultCacheCapBytes;
    }

}

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
