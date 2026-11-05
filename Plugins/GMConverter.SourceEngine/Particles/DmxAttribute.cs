namespace GMConverter.SourceEngine.Particles;

/// <summary>One named value on a <see cref="DmxElement"/>.</summary>
/// <param name="Name">The attribute name, which Source matches case-sensitively.</param>
/// <param name="Type">How <paramref name="Value"/> is stored.</param>
/// <param name="Value">An <see cref="int"/>, <see cref="float"/>, <see cref="bool"/>, <see cref="string"/>, colour <c>byte[4]</c>, <see cref="System.Numerics.Vector3"/>, <see cref="DmxElement"/> or list of elements.</param>
internal sealed record DmxAttribute(string Name, DmxAttributeType Type, object? Value);
