using System.Buffers.Binary;
using System.Collections.Concurrent;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Textures;

/// <summary>
/// Finds the mips a texture keeps outside its package, in the game's texture caches.
/// <para>
/// A mip names its cache (TextureFileCacheName) and an offset, but games may store caches under
/// other file names (CrossFire HD names every file by a hash) and may have grown a cache since the
/// package was cooked, which moves the data by a constant amount. A cache is a run of compressed
/// chunks, one per mip, so the chunks whose stored sizes and spacing match the texture's mips
/// exactly are its mips; that match is unambiguous in practice and survives both changes.
/// </para>
/// </summary>
internal sealed class UE3TextureCaches(IReadOnlyList<string> cacheFiles)
{
    private readonly object _indexLock = new();
    private readonly ConcurrentDictionary<string, (int File, long Shift)> _resolvedCaches = new(StringComparer.OrdinalIgnoreCase);
    private List<CacheFile>? _files;
    private Dictionary<long, List<(int File, long Offset)>>? _chunksBySize;

    /// <summary>The decompressed data of the texture's mip, or null when no cache holds it.</summary>
    public byte[]? ReadExternalMip(UE3Texture2D texture, UE3TextureMip mip)
    {
        var external = texture.Mips.Where(item => item.Data.IsExternal && !item.Data.IsEmpty).ToArray();
        if (external.Length == 0 || !mip.Data.IsExternal || Locate(texture.CacheName, external) is not { } location)
        {
            return null;
        }

        var path = _files![location.File].Path;
        using var stream = File.OpenRead(path);
        return UE3Compression.ReadChunk(stream, mip.Data.OffsetInFile + location.Shift, mip.Data.Compression);
    }

    private (int File, long Shift)? Locate(string? cacheName, UE3TextureMip[] mips)
    {
        EnsureIndex();
        var first = mips[0].Data;
        if (cacheName is not null &&
            _resolvedCaches.TryGetValue(cacheName, out var known) &&
            Matches(known.File, known.Shift, mips))
        {
            return known;
        }

        if (!_chunksBySize!.TryGetValue(first.SizeOnDisk, out var candidates))
        {
            return null;
        }

        foreach (var (file, offset) in candidates)
        {
            var shift = offset - first.OffsetInFile;
            if (Matches(file, shift, mips))
            {
                if (cacheName is not null)
                {
                    _resolvedCaches[cacheName] = (file, shift);
                }

                return (file, shift);
            }
        }

        return null;
    }

    private bool Matches(int file, long shift, UE3TextureMip[] mips)
    {
        var chunks = _files![file].ChunkSizes;
        return mips.All(mip => chunks.TryGetValue(mip.Data.OffsetInFile + shift, out var size) && size == mip.Data.SizeOnDisk);
    }

    private void EnsureIndex()
    {
        lock (_indexLock)
        {
            if (_files is not null)
            {
                return;
            }

            var files = new CacheFile[cacheFiles.Count];
            Parallel.For(0, cacheFiles.Count, index => files[index] = new CacheFile(cacheFiles[index], WalkChunks(cacheFiles[index])));
            Dictionary<long, List<(int File, long Offset)>> bySize = [];
            for (var file = 0; file < files.Length; file++)
            {
                foreach (var (offset, size) in files[file].ChunkSizes)
                {
                    if (!bySize.TryGetValue(size, out var list))
                    {
                        list = [];
                        bySize[size] = list;
                    }

                    list.Add((file, offset));
                }
            }

            _files = [.. files];
            _chunksBySize = bySize;
        }
    }

    private static Dictionary<long, long> WalkChunks(string path)
    {
        Dictionary<long, long> chunks = [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64);
            var length = stream.Length;
            long offset = 0;
            Span<byte> header = stackalloc byte[16];
            while (offset + header.Length <= length)
            {
                stream.Seek(offset, SeekOrigin.Begin);
                stream.ReadExactly(header);
                if (BinaryPrimitives.ReadUInt32LittleEndian(header) != UE3Compression.PackageTag)
                {
                    break;
                }

                var blockSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
                var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
                var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
                if (blockSize <= 0)
                {
                    break;
                }

                var stored = 16 + (uncompressedSize + (long)blockSize - 1) / blockSize * 8 + compressedSize;
                chunks[offset] = stored;
                offset += stored;
            }
        }
        catch (IOException)
        {
            // An unreadable cache just contributes no chunks.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return chunks;
    }

    private sealed record CacheFile(string Path, Dictionary<long, long> ChunkSizes);
}
