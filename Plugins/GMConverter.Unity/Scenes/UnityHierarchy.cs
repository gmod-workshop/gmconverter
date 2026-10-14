using System.Numerics;
using AssetsTools.NET.Extra;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Geometry;

namespace GMConverter.Unity.Scenes;

/// <summary>Loads the transform tree under a root transform, and finds the components on its game objects.</summary>
internal sealed class UnityHierarchy
{
    private readonly UnityAssetLibrary _library;
    private readonly Dictionary<UnityObjectKey, UnityTransformNode> _nodes = [];

    private UnityHierarchy(UnityAssetLibrary library, UnityTransformNode root)
    {
        _library = library;
        Root = root;
    }

    public UnityTransformNode Root { get; }

    public IEnumerable<UnityTransformNode> Nodes => Root.DepthFirst();

    public static UnityHierarchy Load(UnityAssetLibrary library, UnityObject rootTransform)
    {
        var root = CreateNode(library, rootTransform, null, Matrix4x4.Identity, isRoot: true);
        var hierarchy = new UnityHierarchy(library, root);
        hierarchy.Index(root);
        return hierarchy;
    }

    /// <summary>The node for a transform of this hierarchy, or null when the transform lies outside it.</summary>
    public UnityTransformNode? Find(UnityObjectKey transform)
    {
        return _nodes.GetValueOrDefault(transform);
    }

    /// <summary>The components of class <paramref name="classId"/> on <paramref name="node"/>'s game object.</summary>
    public IEnumerable<UnityObject> GetComponents(UnityTransformNode node, AssetClassID classId)
    {
        if (node.GameObject is not { } gameObject)
        {
            yield break;
        }

        foreach (var component in UnityFields.Items(gameObject["m_Component"]))
        {
            var pointer = component["component"];
            if (_library.PeekClass(gameObject.File, pointer) == classId &&
                _library.Resolve(gameObject.File, pointer) is { } resolved)
            {
                yield return resolved;
            }
        }
    }

    private void Index(UnityTransformNode node)
    {
        _nodes[node.Transform.Key] = node;
        foreach (var child in node.Children)
        {
            Index(child);
        }
    }

    private static UnityTransformNode CreateNode(UnityAssetLibrary library, UnityObject transform, UnityTransformNode? parent, Matrix4x4 parentWorld, bool isRoot)
    {
        var gameObject = library.Resolve(transform.File, transform["m_GameObject"]);
        var position = isRoot ? Vector3.Zero : UnitySpace.ReadVector3(transform["m_LocalPosition"]);
        var rotation = isRoot ? Quaternion.Identity : Quaternion.Normalize(UnitySpace.ReadQuaternion(transform["m_LocalRotation"]));
        var scale = isRoot ? Vector3.One : UnitySpace.ReadVector3(transform["m_LocalScale"]);
        var world = UnitySpace.Compose(position, rotation, scale) * parentWorld;
        var isActive = (parent?.IsActive ?? true) && (gameObject?["m_IsActive"].AsBool ?? true);

        var node = new UnityTransformNode
        {
            Transform = transform,
            GameObject = gameObject,
            Name = gameObject?.Name ?? "Transform",
            LocalPosition = position,
            LocalRotation = rotation,
            LocalScale = scale,
            World = world,
            Parent = parent,
            IsActive = isActive,
        };

        foreach (var child in UnityFields.Items(transform["m_Children"]))
        {
            if (library.Resolve(transform.File, child) is { } childTransform)
            {
                node.Children.Add(CreateNode(library, childTransform, node, world, isRoot: false));
            }
        }

        return node;
    }
}
