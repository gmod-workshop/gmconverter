using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine.Materials;

/// <summary>
/// Renames materials whose names Source would misread. studiomdl and the engine drop everything
/// after the last dot of a material name as if it were a file extension, so a Blender-style
/// <c>Body.001</c> looks for <c>Body.vmt</c> and renders as a missing texture. Dots become
/// underscores, and the submeshes and skins naming those materials follow.
/// </summary>
internal static class SourceMaterialNames
{
    public static Model MakeSafe(Model model)
    {
        if (!model.Materials.Any(material => material.Name.Contains('.')) &&
            model.Skins?.Any(skin => skin.Replacements.Keys.Concat(skin.Replacements.Values.Select(material => material.Name)).Any(name => name.Contains('.'))) != true)
        {
            return model;
        }

        HashSet<string> used = new(model.Materials.Select(material => material.Name).Where(name => !name.Contains('.')), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> renames = new(StringComparer.OrdinalIgnoreCase);

        string Rename(string name)
        {
            if (!name.Contains('.'))
            {
                return name;
            }

            if (!renames.TryGetValue(name, out var safe))
            {
                var stem = name.Replace('.', '_');
                safe = stem;
                for (var suffix = 2; !used.Add(safe); suffix++)
                {
                    safe = $"{stem}_{suffix}";
                }

                renames[name] = safe;
            }

            return safe;
        }

        Material RenameMaterial(Material material)
        {
            var name = Rename(material.Name);
            return name == material.Name ? material : material.WithName(name);
        }

        return model with
        {
            Materials = [.. model.Materials.Select(RenameMaterial)],
            Meshes = [.. model.Meshes.Select(mesh => mesh with
            {
                Submeshes = [.. mesh.Submeshes.Select(submesh => submesh.MaterialName is { } name ? submesh with { MaterialName = Rename(name) } : submesh)],
            })],
            Skins = model.Skins is { } skins
                ? [.. skins.Select(skin => skin with
                {
                    Replacements = skin.Replacements.ToDictionary(pair => Rename(pair.Key), pair => RenameMaterial(pair.Value), StringComparer.OrdinalIgnoreCase),
                })]
                : null,
        };
    }
}
