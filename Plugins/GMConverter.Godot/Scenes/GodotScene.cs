using GMConverter.Godot.Packs;
using GMConverter.Godot.Resources;
using GMConverter.SDK.Common;

namespace GMConverter.Godot.Scenes;

/// <summary>
/// Instantiates a <c>PackedScene</c> into a tree of <see cref="GodotSceneNode"/>s, as Godot's
/// <c>SceneState</c> does: each node is created (or, for a node another scene provides, found)
/// under its parent and given its saved properties; nodes that instance another scene bring
/// that scene's nodes with them.
/// </summary>
internal static class GodotScene
{
    private const int _typeInstanced = 0x7FFFFFFF;
    private const int _idIsPath = 1 << 30;
    private const int _instanceIsPlaceholder = 1 << 30;
    private const int _propertyNameMask = (1 << 30) - 1;
    private const int _nameMask = (1 << 18) - 1;
    private const int _maxDepth = 16;

    public static GodotSceneNode Instantiate(GodotResourceLoader loader, GodotResource packedScene)
    {
        return Instantiate(loader, packedScene, null, null, 0);
    }

    // Instantiates the scene with its root under `attachTo` and, when given, named `rootName`.
    private static GodotSceneNode Instantiate(GodotResourceLoader loader, GodotResource packedScene, GodotSceneNode? attachTo, string? rootName, int depth)
    {
        if (depth > _maxDepth)
        {
            throw new GMConverterException("Godot scene instances itself.");
        }

        if (packedScene.Get<Dictionary<object, object?>>("_bundled") is not { } bundle ||
            bundle.GetValueOrDefault("names") is not string[] names ||
            bundle.GetValueOrDefault("variants") is not List<object?> variants ||
            bundle.GetValueOrDefault("nodes") is not int[] data)
        {
            throw new GMConverterException($"Godot {packedScene.Type} has no scene.");
        }

        var nodePaths = bundle.GetValueOrDefault("node_paths") as List<object?> ?? [];
        var count = bundle.GetValueOrDefault("node_count") is long nodeCount ? (int)nodeCount : 0;
        var nodes = new GodotSceneNode?[count];
        GodotSceneNode? root = null;
        var position = 0;

        for (var index = 0; index < count; index++)
        {
            var parentId = data[position++];
            position++; // owner
            var type = data[position++];
            var name = names[data[position++] & _nameMask];
            if (index == 0 && rootName is not null)
            {
                name = rootName;
            }

            var instance = data[position++];
            var properties = new (string Name, object? Value)[data[position++]];
            for (var property = 0; property < properties.Length; property++)
            {
                properties[property] = (names[data[position] & _propertyNameMask], variants[data[position + 1]]);
                position += 2;
            }

            position += data[position] + 1; // groups

            var parent = parentId < 0
                ? attachTo
                : (parentId & _idIsPath) != 0
                    ? (nodePaths.ElementAtOrDefault(parentId & ~_idIsPath) as GodotNodePath) is { } path ? root?.Find(path.Names) : null
                    : nodes.ElementAtOrDefault(parentId);

            GodotSceneNode? node;
            if (instance >= 0 && (instance & _instanceIsPlaceholder) == 0 && loader.Get(variants[instance]) is { } instanced)
            {
                // The instanced scene's root takes this node's name and place.
                node = Instantiate(loader, instanced, parent, name, depth + 1);
            }
            else if (type == _typeInstanced)
            {
                // A node an inherited or instanced scene already made: find it to set its properties.
                node = index == 0 && bundle.GetValueOrDefault("base_scene") is long baseScene && loader.Get(variants[(int)baseScene]) is { } inherited
                    ? Instantiate(loader, inherited, parent, name, depth + 1)
                    : parent?.Children.FirstOrDefault(child => child.Name == name);
            }
            else
            {
                node = new GodotSceneNode(name, names[type], parent);
                parent?.Children.Add(node);
            }

            if (node is null)
            {
                continue;
            }

            foreach (var (propertyName, value) in properties)
            {
                node.Properties[propertyName] = value;
            }

            nodes[index] = node;
            root ??= node;
        }

        return root ?? throw new GMConverterException("Godot scene has no nodes.");
    }
}
