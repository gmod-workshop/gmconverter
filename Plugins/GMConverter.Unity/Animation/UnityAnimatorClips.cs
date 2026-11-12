using AssetsTools.NET.Extra;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Animation;

/// <summary>The clips an Animator's controller plays, following override controllers to their base.</summary>
internal static class UnityAnimatorClips
{
    public static IReadOnlyList<UnityObject> Read(UnityAssetLibrary library, UnityObject animator)
    {
        return library.Resolve(animator.File, animator["m_Controller"]) is { } controller
            ? [.. FromController(library, controller, depth: 0).DistinctBy(clip => clip.Key)]
            : [];
    }

    private static IEnumerable<UnityObject> FromController(UnityAssetLibrary library, UnityObject controller, int depth)
    {
        if (controller.ClassId == AssetClassID.AnimatorController)
        {
            return UnityFields.Items(controller["m_AnimationClips"])
                .Select(pointer => library.Resolve(controller.File, pointer))
                .OfType<UnityObject>();
        }

        if (controller.ClassId != AssetClassID.AnimatorOverrideController || depth > 8 ||
            library.Resolve(controller.File, controller["m_Controller"]) is not { } baseController)
        {
            return [];
        }

        Dictionary<UnityObjectKey, UnityObject> overrides = [];
        foreach (var pair in UnityFields.Items(controller["m_Clips"]))
        {
            if (library.ResolveKey(controller.File, pair["m_OriginalClip"]) is { } original &&
                library.Resolve(controller.File, pair["m_OverrideClip"]) is { } replacement)
            {
                overrides[original] = replacement;
            }
        }

        return FromController(library, baseController, depth + 1).Select(clip => overrides.GetValueOrDefault(clip.Key, clip));
    }
}
