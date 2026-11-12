using System.Numerics;

namespace GMConverter.SourceEngine.Particles;

/// <summary>An element of a DMX file (a particle system, an operator): a type, a name, an id and ordered attributes.</summary>
internal sealed class DmxElement(string type, string name)
{
    private readonly List<DmxAttribute> _attributes = [];

    public string Type { get; } = type;

    public string Name { get; } = name;

    public IReadOnlyList<DmxAttribute> Attributes => _attributes;

    public DmxElement Set(string name, int value) => Add(name, DmxAttributeType.Int, value);

    public DmxElement Set(string name, float value) => Add(name, DmxAttributeType.Float, value);

    public DmxElement Set(string name, bool value) => Add(name, DmxAttributeType.Bool, value);

    public DmxElement Set(string name, string value) => Add(name, DmxAttributeType.String, value);

    public DmxElement Set(string name, Vector3 value) => Add(name, DmxAttributeType.Vector3, value);

    public DmxElement Set(string name, DmxElement? value) => Add(name, DmxAttributeType.Element, value);

    public DmxElement Set(string name, IReadOnlyList<DmxElement> value) => Add(name, DmxAttributeType.ElementArray, value);

    /// <summary>Sets an RGBA colour, 0 to 255 per channel.</summary>
    public DmxElement SetColor(string name, byte red, byte green, byte blue, byte alpha) =>
        Add(name, DmxAttributeType.Color, new[] { red, green, blue, alpha });

    /// <summary>The value of <paramref name="name"/>, or null when it isn't set.</summary>
    public object? Get(string name) => _attributes.FirstOrDefault(attribute => attribute.Name == name)?.Value;

    private DmxElement Add(string name, DmxAttributeType type, object? value)
    {
        _attributes.RemoveAll(attribute => attribute.Name == name);
        _attributes.Add(new DmxAttribute(name, type, value));
        return this;
    }
}
