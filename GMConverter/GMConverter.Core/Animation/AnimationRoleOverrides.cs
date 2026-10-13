using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;

namespace GMConverter.Core.Animation;

/// <summary>
/// Applies roles the user chose for animations by clip name, replacing whatever the importer or
/// the source game's profile guessed. A null role clears the clip's role.
/// </summary>
internal static class AnimationRoleOverrides
{
    public static Model Apply(Model model, IReadOnlyDictionary<string, AnimationRole?> overrides)
    {
        if (overrides.Count == 0)
        {
            return model;
        }

        var clips = model.Animations ?? [];
        var unknown = overrides.Keys
            .Where(name => !clips.Any(clip => string.Equals(clip.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new GMConverterException(
                $"Role overrides name animations the model doesn't have: {string.Join(", ", unknown)}. " +
                $"Animations: {(clips.Count == 0 ? "none" : string.Join(", ", clips.Select(clip => clip.Name)))}.");
        }

        var byName = new Dictionary<string, AnimationRole?>(overrides, StringComparer.OrdinalIgnoreCase);
        return model with
        {
            Animations = [.. clips.Select(clip => byName.TryGetValue(clip.Name, out var role) ? clip with { Role = role } : clip)],
        };
    }
}
