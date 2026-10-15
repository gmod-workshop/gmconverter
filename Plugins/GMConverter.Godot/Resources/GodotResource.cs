namespace GMConverter.Godot.Resources;

/// <summary>
/// A resource read from a binary resource file: its class and the properties it saved. Values are
/// <see cref="bool"/>, <see cref="long"/>, <see cref="double"/>, <see cref="string"/>, the
/// <c>System.Numerics</c> vectors, quaternions and planes, the <c>Godot*</c> value records,
/// packed arrays as .NET arrays, <see cref="List{T}"/> and <see cref="Dictionary{TKey,TValue}"/>,
/// another <see cref="GodotResource"/>, a <see cref="GodotExternalResource"/>, or null.
/// </summary>
internal sealed class GodotResource(string type, string id, IReadOnlyDictionary<string, object?> properties)
{
    public string Type { get; } = type;

    /// <summary>The resource's id within its file (the part after <c>local://</c>), or its own path.</summary>
    public string Id { get; } = id;

    public IReadOnlyDictionary<string, object?> Properties { get; } = properties;

    public object? this[string name] => Properties.GetValueOrDefault(name);

    public T? Get<T>(string name)
    {
        return Properties.GetValueOrDefault(name) is T value ? value : default;
    }

    public override string ToString()
    {
        return $"{Type} {Id}";
    }
}
