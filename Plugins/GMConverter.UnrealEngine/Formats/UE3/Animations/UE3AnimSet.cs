using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Animations;

/// <summary>
/// A UE3 AnimSet: the bones its sequences animate and how their translation is used. With
/// <c>RotationOnly</c>, only bones in <c>TranslatedBones</c> keep the animation's translation and
/// the rest keep the mesh's own, so one set fits meshes of different proportions. Cooked packages
/// leave out properties at their class default, and AnimSet's default is rotation-only.
/// </summary>
internal sealed record UE3AnimSet(
    string Name,
    IReadOnlyList<string> TrackBones,
    IReadOnlyList<int> Sequences,
    bool RotationOnly,
    IReadOnlyList<string> TranslatedBones,
    string? PreviewMeshName)
{
    public static UE3AnimSet Read(UE3Package package, UE3PackageExport export)
    {
        var properties = package.OpenObject(export).Properties;
        var forceMesh = new HashSet<string>(properties.GetNameArray("ForceMeshTranslationBoneNames"), StringComparer.OrdinalIgnoreCase);
        return new UE3AnimSet(
            export.ObjectName,
            properties.GetNameArray("TrackBoneNames"),
            properties.GetObjectArray("Sequences"),
            properties.GetBool("bAnimRotationOnly") ?? true,
            [.. properties.GetNameArray("UseTranslationBoneNames").Where(bone => !forceMesh.Contains(bone))],
            properties.GetName("PreviewSkelMeshName"));
    }
}
