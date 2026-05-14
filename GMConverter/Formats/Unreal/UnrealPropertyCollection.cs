namespace GMConverter.Formats.Unreal;

internal sealed class UnrealPropertyCollection
{
    private readonly Dictionary<string, List<int>> _integers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<int>> _objectReferences = new(StringComparer.OrdinalIgnoreCase);

    public void AddInteger(string propertyName, int value)
    {
        AddValue(_integers, propertyName, value);
    }

    public void AddName(string propertyName, string value)
    {
        AddValue(_names, propertyName, value);
    }

    public void AddObjectReference(string propertyName, int packageIndex)
    {
        AddValue(_objectReferences, propertyName, packageIndex);
    }

    public int? FirstInteger(string propertyName)
    {
        return _integers.TryGetValue(propertyName, out var values) && values.Count > 0
            ? values[0]
            : null;
    }

    public string? FirstName(string propertyName)
    {
        return _names.TryGetValue(propertyName, out var values) && values.Count > 0
            ? values[0]
            : null;
    }

    public int? FirstObjectReference(string propertyName)
    {
        return _objectReferences.TryGetValue(propertyName, out var values) && values.Count > 0
            ? values[0]
            : null;
    }

    public IReadOnlyList<int> ObjectReferences(string propertyName)
    {
        return _objectReferences.TryGetValue(propertyName, out var values)
            ? values
            : [];
    }

    private static void AddValue<T>(Dictionary<string, List<T>> values, string propertyName, T value)
    {
        if (!values.TryGetValue(propertyName, out var existingValues))
        {
            existingValues = [];
            values[propertyName] = existingValues;
        }

        existingValues.Add(value);
    }
}
