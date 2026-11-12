using AssetsTools.NET.Extra;
using GMConverter.SDK.Animation;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Models;
using GMConverter.Unity.Scenes;

namespace GMConverter.Unity.Animation;

/// <summary>
/// The Animators in a hierarchy and the clips to import for each. Each Animator plays its
/// controller's clips from its own game object, so a prefab holding a character and a prop with
/// their own Animators gets both sets. Clips named in the request go to the humanoid Animator, or
/// the first one.
/// </summary>
internal sealed class UnityAnimators
{
    private readonly List<Entry> _entries;
    private readonly List<string> _skipped;

    private UnityAnimators(List<Entry> entries, List<string> skipped)
    {
        _entries = entries;
        _skipped = skipped;
    }

    /// <summary>Clips left out, with the reason.</summary>
    public IReadOnlyList<string> Skipped => _skipped;

    public static UnityAnimators Find(UnityAssetLibrary library, UnityHierarchy hierarchy, UnityAnimationRequest request)
    {
        List<Entry> entries = [];
        List<string> skipped = [];
        foreach (var node in hierarchy.Nodes)
        {
            foreach (var animator in hierarchy.GetComponents(node, AssetClassID.Animator))
            {
                var avatar = library.Resolve(animator.File, animator["m_Avatar"]) is { } avatarObject ? UnityAvatar.Read(avatarObject) : null;
                entries.Add(new Entry(node, avatar, request.IncludeController ? [.. UnityAnimatorClips.Read(library, animator)] : []));
            }
        }

        if (request.Clips.Count > 0)
        {
            var target = entries.Find(entry => entry.Avatar is not null) ?? entries.FirstOrDefault();
            if (target is null)
            {
                target = new Entry(hierarchy.Root, null, []);
                entries.Add(target);
            }

            foreach (var key in request.Clips)
            {
                if (library.Find(key) is { ClassId: AssetClassID.AnimationClip } clip)
                {
                    target.Clips.Add(clip);
                }
                else
                {
                    skipped.Add($"{key} (not an animation clip)");
                }
            }
        }

        foreach (var entry in entries)
        {
            entry.Clips.RemoveAll(clip =>
            {
                if (!clip["m_Legacy"].AsBool)
                {
                    return false;
                }

                skipped.Add($"{clip.Name} (legacy clip)");
                return true;
            });
        }

        return new UnityAnimators(entries, skipped);
    }

    /// <summary>The nodes the clips move with transform curves; props among them need bones of their own.</summary>
    public IReadOnlySet<UnityTransformNode> MovedNodes()
    {
        return Targets(binding => binding.TransformCurve != UnityTransformCurve.None);
    }

    /// <summary>The nodes the clips turn on and off; those drawing something need bones of their own.</summary>
    public IReadOnlySet<UnityTransformNode> ToggledNodes()
    {
        return Targets(binding => binding.IsActiveToggle);
    }

    private HashSet<UnityTransformNode> Targets(Func<UnityClipBinding, bool> predicate)
    {
        HashSet<UnityTransformNode> targets = [];
        foreach (var entry in _entries.Where(entry => entry.Clips.Count > 0))
        {
            var nodes = UnityAnimationBuilder.NodesByPathHash(entry.Node);
            foreach (var binding in entry.Clips.SelectMany(clip => UnityClipBinding.Read(clip["m_ClipBindingConstant"])))
            {
                if (predicate(binding) && nodes.TryGetValue(binding.Path, out var node))
                {
                    targets.Add(node);
                }
            }
        }

        return targets;
    }

    public List<AnimationClip>? Build(UnitySkeleton skeleton, IReadOnlySet<UnityTransformNode> toggledBones)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<UnityObjectKey> built = [];
        List<AnimationClip> animations = [];
        foreach (var entry in _entries.Where(entry => entry.Clips.Count > 0))
        {
            var builder = new UnityAnimationBuilder(skeleton, entry.Node, entry.Avatar, toggledBones);
            foreach (var clip in entry.Clips.Where(clip => built.Add(clip.Key)))
            {
                if (builder.Build(clip, UniqueName(names, clip.Name)) is { } animation)
                {
                    animations.Add(animation);
                }
                else
                {
                    _skipped.Add(!builder.IsHumanoid && UnityAnimationBuilder.HasHumanoidCurves(clip)
                        ? $"{clip.Name} (humanoid clip on a model without a humanoid avatar)"
                        : $"{clip.Name} (animates nothing in this model)");
                }
            }
        }

        return animations.Count == 0 ? null : animations;
    }

    private static string UniqueName(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var suffix = 2; !used.Add(candidate); suffix++)
        {
            candidate = $"{name}_{suffix}";
        }

        return candidate;
    }

    private sealed record Entry(UnityTransformNode Node, UnityAvatar? Avatar, List<UnityObject> Clips);
}
