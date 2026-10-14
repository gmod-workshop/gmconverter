using AssetsTools.NET;
using GMConverter.Unity.Assets;

namespace GMConverter.Unity.Animation;

/// <summary>
/// The humanoid half of a Unity <c>Avatar</c>: the human skeleton (the avatar's bones on the paths
/// between its human bones), its reference pose, each bone's muscle frame, and which skeleton
/// node each human bone is. Everything is in Unity space, relative to the animated root.
/// </summary>
internal sealed class UnityAvatar
{
    /// <summary>Body bones in Mecanim's order: hips, legs, spine, neck and head, arms, toes, eyes, jaw.</summary>
    public const int BodyBoneCount = 25;

    /// <summary>Finger bones per hand: three phalanges for each of five fingers, thumb first.</summary>
    public const int HandBoneCount = 15;

    private UnityAvatar(
        string?[] nodePaths,
        int[] parents,
        UnityAvatarAxes?[] axes,
        UnityXform[] pose,
        int[] bodyNodes,
        int[] leftHandNodes,
        int[] rightHandNodes,
        float[] bodyMasses,
        float scale,
        UnityXform root)
    {
        NodePaths = nodePaths;
        Parents = parents;
        Axes = axes;
        Pose = pose;
        BodyNodes = bodyNodes;
        LeftHandNodes = leftHandNodes;
        RightHandNodes = rightHandNodes;
        BodyMasses = bodyMasses;
        Scale = scale;
        Root = root;
    }

    /// <summary>Each human skeleton node's transform path below the animated root, when the avatar names it.</summary>
    public IReadOnlyList<string?> NodePaths { get; }

    public IReadOnlyList<int> Parents { get; }

    /// <summary>The muscle frame of each node that is a human bone.</summary>
    public IReadOnlyList<UnityAvatarAxes?> Axes { get; }

    /// <summary>The human skeleton's reference pose, local to each node's parent.</summary>
    public IReadOnlyList<UnityXform> Pose { get; }

    /// <summary>The node of each body bone, or -1 when the avatar lacks it (such as an upper chest).</summary>
    public IReadOnlyList<int> BodyNodes { get; }

    public IReadOnlyList<int> LeftHandNodes { get; }

    public IReadOnlyList<int> RightHandNodes { get; }

    /// <summary>Each body bone's share of the body's mass, for finding its centre of mass.</summary>
    public IReadOnlyList<float> BodyMasses { get; }

    /// <summary>The human scale: root positions in clips are multiplied by it.</summary>
    public float Scale { get; }

    /// <summary>The body's centre of mass and orientation in the reference pose.</summary>
    public UnityXform Root { get; }

    public int NodeCount => Parents.Count;

    /// <summary>Reads the avatar, or returns null when it is not humanoid.</summary>
    public static UnityAvatar? Read(UnityObject avatar)
    {
        var constant = avatar["m_Avatar"];
        var human = constant["m_Human"]["data"];
        var skeleton = human["m_Skeleton"]["data"];
        var nodes = UnityFields.Items(skeleton["m_Node"]);
        if (nodes.Count == 0)
        {
            return null;
        }

        var names = UnityFields.Items(avatar["m_TOS"]).ToDictionary(pair => pair["first"].AsUInt, pair => pair["second"].AsString);
        var ids = UnityFields.Items(skeleton["m_ID"]).Select(id => id.AsUInt).ToArray();
        var axesArray = UnityFields.Items(skeleton["m_AxesArray"]).Select(UnityAvatarAxes.Read).ToArray();
        var pose = UnityFields.Items(human["m_SkeletonPose"]["data"]["m_X"]).Select(UnityXform.Read).ToArray();

        var parents = new int[nodes.Count];
        var axes = new UnityAvatarAxes?[nodes.Count];
        var paths = new string?[nodes.Count];
        for (var node = 0; node < nodes.Count; node++)
        {
            parents[node] = nodes[node]["m_ParentId"].AsInt;
            var axesId = nodes[node]["m_AxesId"].AsInt;
            axes[node] = axesId >= 0 && axesId < axesArray.Length ? axesArray[axesId] : null;
            paths[node] = node < ids.Length && names.TryGetValue(ids[node], out var path) ? path : null;
        }

        return new UnityAvatar(
            paths,
            parents,
            axes,
            pose,
            ReadIndices(human["m_HumanBoneIndex"], BodyBoneCount),
            human["m_HasLeftHand"].AsBool ? ReadIndices(human["m_LeftHand"]["data"]["m_HandBoneIndex"], HandBoneCount) : Missing(HandBoneCount),
            human["m_HasRightHand"].AsBool ? ReadIndices(human["m_RightHand"]["data"]["m_HandBoneIndex"], HandBoneCount) : Missing(HandBoneCount),
            [.. UnityFields.Items(human["m_HumanBoneMass"]).Select(mass => mass.AsFloat)],
            human["m_Scale"].AsFloat,
            UnityXform.Read(human["m_RootX"]));
    }

    private static int[] ReadIndices(AssetTypeValueField field, int count)
    {
        var indices = UnityFields.Items(field).Select(index => index.AsInt).ToArray();
        return indices.Length >= count ? indices : [.. indices, .. Missing(count - indices.Length)];
    }

    private static int[] Missing(int count)
    {
        return [.. Enumerable.Repeat(-1, count)];
    }
}
