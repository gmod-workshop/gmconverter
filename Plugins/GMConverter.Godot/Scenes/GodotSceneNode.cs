using System.Numerics;
using GMConverter.Godot.Resources;

namespace GMConverter.Godot.Scenes;

/// <summary>A node of an instantiated Godot scene, with the properties the scene (and any scene it instances) set on it.</summary>
internal sealed class GodotSceneNode(string name, string type, GodotSceneNode? parent)
{
    public string Name { get; set; } = name;

    /// <summary>The node's class, such as <c>MeshInstance3D</c>.</summary>
    public string Type { get; set; } = type;

    public GodotSceneNode? Parent { get; } = parent;

    public List<GodotSceneNode> Children { get; } = [];

    public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    public object? this[string property] => Properties.GetValueOrDefault(property);

    /// <summary>The transform relative to the parent: <c>transform</c>, or its parts as Godot also saves them.</summary>
    public Matrix4x4 Local
    {
        get
        {
            if (this["transform"] is GodotTransform3D transform)
            {
                return transform.ToMatrix();
            }

            var scale = this["scale"] is Vector3 savedScale ? savedScale : Vector3.One;
            var rotation = this["quaternion"] is Quaternion quaternion ? quaternion : Quaternion.Identity;
            var position = this["position"] is Vector3 savedPosition ? savedPosition : Vector3.Zero;
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }
    }

    public Matrix4x4 World => Parent is null ? Local : Local * Parent.World;

    /// <summary>False when the node or a node above it is hidden.</summary>
    public bool IsVisible => this["visible"] is not false && (Parent?.IsVisible ?? true);

    public IEnumerable<GodotSceneNode> Ancestors()
    {
        for (var node = Parent; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }

    /// <summary>This node and every node below it, parents first.</summary>
    public IEnumerable<GodotSceneNode> Descendants()
    {
        yield return this;
        foreach (var descendant in Children.SelectMany(child => child.Descendants()))
        {
            yield return descendant;
        }
    }

    /// <summary>The node a node path names from this one (<c>..</c> for the parent), or null.</summary>
    public GodotSceneNode? Find(IEnumerable<string> names)
    {
        var node = this;
        foreach (var name in names)
        {
            node = name switch
            {
                "." => node,
                ".." => node.Parent,
                _ => node.Children.FirstOrDefault(child => child.Name == name),
            };
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    public override string ToString()
    {
        return $"{Name} ({Type})";
    }
}
