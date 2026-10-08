namespace GMConverter.SDK.Materials;

/// <summary>
/// Alternate look for a model, such as a damaged or powered-down state. <see cref="Replacements"/>
/// maps the name of a material the meshes use to the material shown in its place; materials it
/// doesn't list keep their default.
/// </summary>
public sealed record MaterialSkin(string Name, IReadOnlyDictionary<string, Material> Replacements);
