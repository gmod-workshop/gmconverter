namespace GMConverter.UnrealEngine.Formats.UE3.Packages;

/// <summary>
/// One tagged property of an object: its name and type, and where its value lies in the package
/// data. Byte properties holding an enum keep the enum value's name in <see cref="EnumValue"/>,
/// and bool properties keep their value in <see cref="BoolValue"/>, since neither sits in the value bytes.
/// </summary>
internal sealed record UE3Property(
    string Name,
    string Type,
    int ArrayIndex,
    int ValueOffset,
    int Size,
    string? StructName = null,
    string? EnumValue = null,
    bool BoolValue = false);
