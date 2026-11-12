using GMConverter.SDK.Geometry;
using GMConverter.SDK.Materials;

namespace GMConverter.UnrealEngine.Importers;

// Adds other meshes made for the same skeleton (such as Zero Company's Coil B1 next to the plain
// B1) to a model as a "body" body group, so one model can show any of them. Bones are matched by
// name, as for the parts of a scene; a variant bone the model lacks is added to its skeleton.
internal static class PSKVariants
{
    public const string GroupName = "body";

    /// <param name="model">The model the variants are added to.</param>
    /// <param name="modelOptionName">The name of the option that shows the model's own meshes.</param>
    /// <param name="variants">The variants and the names of their options.</param>
    public static Model Attach(Model model, string modelOptionName, IReadOnlyList<(string Name, Model Model)> variants)
    {
        if (variants.Count == 0)
        {
            return model;
        }

        var (skeleton, parts) = PSKSceneSkeleton.Merge([model, .. variants.Select(variant => variant.Model)]);
        List<Mesh> meshes = [.. parts[0].Meshes];
        List<Material> materials = [.. model.Materials];
        HashSet<string> materialNames = new(materials.Select(material => material.Name), StringComparer.OrdinalIgnoreCase);
        List<BodyGroupOption> options = [new BodyGroupOption(modelOptionName, [.. Enumerable.Range(0, meshes.Count)])];
        for (var index = 1; index < parts.Count; index++)
        {
            var variant = parts[index];
            var first = meshes.Count;
            meshes.AddRange(variant.Meshes);
            options.Add(new BodyGroupOption(variants[index - 1].Name, [.. Enumerable.Range(first, variant.Meshes.Count)]));
            foreach (var material in variant.Materials.Where(material => materialNames.Add(material.Name)))
            {
                materials.Add(material);
            }
        }

        return model with
        {
            Meshes = meshes,
            Materials = materials,
            Skeleton = skeleton ?? model.Skeleton,
            BodyGroups = [.. model.BodyGroups ?? [], new BodyGroup(GroupName, options)],
        };
    }
}
