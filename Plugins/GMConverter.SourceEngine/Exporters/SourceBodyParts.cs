using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;

namespace GMConverter.SourceEngine.Exporters;

/// <summary>
/// Splits a model's meshes into Source body parts: the meshes no body group switches form the
/// always-shown <c>$body</c>, and each <see cref="BodyGroup"/> becomes a <c>$bodygroup</c> with an
/// SMD per option (or <c>blank</c> for an option that shows nothing).
/// </summary>
internal static class SourceBodyParts
{
    private const string _bodyName = "body";
    private const string _baseName = "base";

    public static IReadOnlyList<SourceBodyPart> Plan(Model model, string safeBaseName)
    {
        var groups = model.BodyGroups ?? [];
        var grouped = groups.SelectMany(group => group.Options.SelectMany(option => option.MeshIndices)).ToHashSet();
        var always = model.Meshes.Where((_, index) => !grouped.Contains(index)).ToArray();

        List<SourceBodyPart> parts = [];
        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase) { $"{safeBaseName}.smd" };
        if (always.Length > 0 || groups.Count == 0)
        {
            // studiomdl needs body part names to be unique, so the always-shown part steps aside for
            // a group that is itself called "body".
            var name = groups.Any(group => string.Equals(group.Name, _bodyName, StringComparison.OrdinalIgnoreCase)) ? _baseName : _bodyName;
            parts.Add(new SourceBodyPart(name, [new SourceBodyPartOption($"{safeBaseName}.smd", always)], IsGroup: false));
        }

        foreach (var group in groups)
        {
            List<SourceBodyPartOption> options = [];
            foreach (var option in group.Options)
            {
                var meshes = option.MeshIndices.Where(index => index >= 0 && index < model.Meshes.Count).Select(index => model.Meshes[index]).ToArray();
                options.Add(new SourceBodyPartOption(meshes.Length == 0 ? null : UniqueFileName(safeBaseName, group.Name, option.Name, fileNames), meshes));
            }

            if (options.Count > 0)
            {
                parts.Add(new SourceBodyPart(group.Name, options, IsGroup: true));
            }
        }

        return parts;
    }

    private static string UniqueFileName(string safeBaseName, string groupName, string optionName, HashSet<string> used)
    {
        var stem = $"{safeBaseName}_{NameHelpers.SanitizeMaterialName(groupName)}_{NameHelpers.SanitizeMaterialName(optionName)}";
        var fileName = $"{stem}.smd";
        for (var index = 2; !used.Add(fileName); index++)
        {
            fileName = $"{stem}_{index}.smd";
        }

        return fileName;
    }
}
