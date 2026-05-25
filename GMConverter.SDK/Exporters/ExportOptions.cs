using System.Globalization;

namespace GMConverter.SDK.Exporters;

/// <summary>
/// Opaque-but-typed-accessor bag of option values handed to an exporter at invocation time.
/// The host builds it from UI / CLI inputs (parsed values keyed by <see cref="OptionDescriptor.Key"/>);
/// the exporter reads what it needs via the typed Get helpers and constructs its own internal
/// strongly-typed options record from the result. This keeps the SDK contract opaque while
/// letting plugin code stay type-safe internally.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>An empty bag — no keys present. Useful for default invocations.</summary>
    public static ExportOptions Empty { get; } = new(new Dictionary<string, object?>());

    private readonly IReadOnlyDictionary<string, object?> _values;

    public ExportOptions(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
    }

    /// <summary>True if the bag contains a value (possibly <c>null</c>) for the given key.</summary>
    public bool Contains(string key)
    {
        return _values.ContainsKey(key);
    }

    /// <summary>
    /// Returns the value as a string, or <c>null</c> if not present or set to <c>null</c>.
    /// Non-string values are converted via <c>ToString()</c> so callers can read primitive values
    /// the host stored as their native CLR type (e.g. an <c>int</c> arrived from System.CommandLine).
    /// </summary>
    public string? GetString(string key)
    {
        if (!_values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }
        return value as string ?? value.ToString();
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        if (!_values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }
        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        if (!_values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }
        return value switch
        {
            int i => i,
            long l => checked((int)l),
            short s => s,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    public float GetFloat(string key, float defaultValue = 0f)
    {
        if (!_values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Reference-type accessor for non-primitive values the host may have stored directly (rare —
    /// the schema is designed around primitives, but this escape hatch exists for plugins that
    /// want to round-trip complex objects across an invocation).
    /// </summary>
    public T? Get<T>(string key) where T : class
    {
        return _values.TryGetValue(key, out var value) ? value as T : null;
    }

    /// <summary>The raw underlying dictionary. Useful for diagnostics and host-side iteration.</summary>
    public IReadOnlyDictionary<string, object?> AsDictionary()
    {
        return _values;
    }
}
