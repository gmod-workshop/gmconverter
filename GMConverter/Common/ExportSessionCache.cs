using System.Collections.Concurrent;

namespace GMConverter.Common;

// Per-operation memoization for expensive value-producing work — typically texture decode, but the
// shape is intentionally generic so importers and exporters that aren't UE-specific (MOW, OPT, MDL,
// future formats) can use the same primitive. A caller scopes the cache around one user operation
// (one resolve / preview / convert), keys each expensive value by a (category, identity) pair, and
// supplies a lambda that produces the value on first request.
//
// Why this exists: multi-part exports frequently reference the same source assets across parts —
// e.g. a 22-part Fortnite Playset prop where five textures are reused by every part. The naïve
// path decodes those textures once per reference; this cache collapses that to once per session.
// Both the keying scheme and the value type are deliberately format-agnostic. The `category`
// segment exists so unrelated cache consumers don't collide on identity strings — e.g. one
// importer may cache by file path while another caches by an internal asset path that could
// theoretically clash with the first.
//
// Thread-safety: backed by ConcurrentDictionary, with values wrapped in Lazy&lt;T&gt; under
// ExecutionAndPublication mode so the producer lambda runs at most once per key even when many
// threads race to populate the same entry. This is the property that makes the cache play
// correctly with the parallel-parts work landing in a later tier.
internal sealed class ExportSessionCache
{
    // Values are stored as object so a single cache instance can hold heterogeneous payloads —
    // PNG byte arrays for one consumer, structured DecodedImage records for another. The category
    // segment of the key is the implicit type discriminator; callers that pass different T to the
    // same (category, identity) will see an InvalidCastException, which is by design.
    private readonly ConcurrentDictionary<CacheKey, Lazy<object>> _entries = new();
    private int _hitCount;
    private int _missCount;

    public int Count => _entries.Count;

    public int HitCount => Volatile.Read(ref _hitCount);

    public int MissCount => Volatile.Read(ref _missCount);

    public T GetOrCompute<T>(string category, string identity, Func<T> compute)
        where T : class
    {
        var key = new CacheKey(category, identity);

        if (_entries.TryGetValue(key, out var existingLazy))
        {
            Interlocked.Increment(ref _hitCount);
            return (T)existingLazy.Value;
        }

        var addedLazy = _entries.GetOrAdd(
            key,
            _ => new Lazy<object>(() => compute(), LazyThreadSafetyMode.ExecutionAndPublication));

        if (ReferenceEquals(addedLazy, existingLazy))
        {
            Interlocked.Increment(ref _hitCount);
        }
        else
        {
            Interlocked.Increment(ref _missCount);
        }

        return (T)addedLazy.Value;
    }

    // Convenience overload for byte[] producers — keeps the Tier 1 call sites readable and avoids
    // forcing every caller to spell out the type argument when the cached value is just bytes.
    public byte[] GetOrDecode(string category, string identity, Func<byte[]> decode)
        => GetOrCompute(category, identity, decode);

    public bool TryGet<T>(string category, string identity, out T? value)
        where T : class
    {
        if (_entries.TryGetValue(new CacheKey(category, identity), out var lazy))
        {
            value = (T)lazy.Value;
            return true;
        }
        value = null;
        return false;
    }

    private readonly record struct CacheKey(string Category, string Identity);
}
