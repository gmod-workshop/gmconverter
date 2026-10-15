namespace GMConverter.Godot.Resources;

/// <summary>
/// A node path such as <c>Skeleton3D:Hips</c>: the node names, then the property or bone names
/// after the colons.
/// </summary>
internal sealed record GodotNodePath(IReadOnlyList<string> Names, IReadOnlyList<string> SubNames, bool IsAbsolute)
{
    public override string ToString()
    {
        var path = (IsAbsolute ? "/" : string.Empty) + string.Join('/', Names);
        return SubNames.Count == 0 ? path : path + ":" + string.Join(':', SubNames);
    }
}
