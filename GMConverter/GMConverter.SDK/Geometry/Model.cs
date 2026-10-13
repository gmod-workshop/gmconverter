using System.Numerics;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Assets;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Physics;
using GMConverter.SDK.Textures;

namespace GMConverter.SDK.Geometry;

/// <summary>
/// An engine-neutral model: meshes and materials, and optionally a skeleton, animations, skins,
/// attachments, physics, blend spaces and body groups. Model space is Z-up. <c>Forward</c> is the
/// direction the model faces in that space (a character's front), when the importer knows it;
/// exporters for engines with a facing convention turn the model about Z to match, and null leaves
/// it as is. <c>Meshes</c> holds every mesh, including the alternatives of <c>BodyGroups</c>;
/// <see cref="DefaultMeshes"/> is what shows when no body group is changed.
/// </summary>
public sealed record Model(
    string Name,
    IReadOnlyList<Mesh> Meshes,
    IReadOnlyList<Material> Materials,
    Skeleton? Skeleton = null,
    IReadOnlyList<AnimationClip>? Animations = null,
    IReadOnlyList<MaterialSkin>? Skins = null,
    IReadOnlyList<Attachment>? Attachments = null,
    ModelPhysics? Physics = null,
    IReadOnlyList<AnimationBlendSpace>? BlendSpaces = null,
    Vector3? Forward = null,
    IReadOnlyList<BodyGroup>? BodyGroups = null)
    : IAsset
{
    AssetKind IAsset.Kind => AssetKind.Model;

    /// <summary>
    /// The meshes that show by default: every mesh except those only a body group's other options
    /// show. Exporters for formats without body groups write these.
    /// </summary>
    public IReadOnlyList<Mesh> DefaultMeshes
    {
        get
        {
            if (BodyGroups is not { Count: > 0 } groups)
            {
                return Meshes;
            }

            var hidden = groups
                .SelectMany(group => group.Options.Skip(1).SelectMany(option => option.MeshIndices))
                .Except(groups.SelectMany(group => group.Options.Take(1).SelectMany(option => option.MeshIndices)))
                .ToHashSet();
            return [.. Meshes.Where((_, index) => !hidden.Contains(index))];
        }
    }

    public IReadOnlyList<Texture> Textures => Materials
        .SelectMany(material => material.Textures)
        .DistinctBy(texture => texture.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public Bounds Bounds()
    {
        return SDK.Geometry.Bounds.FromPoints(DefaultMeshes.SelectMany(mesh => mesh.Positions).ToArray());
    }

    public Mesh Merge()
    {
        List<Vertex> vertices = [];
        List<Triangle> triangles = [];

        foreach (var mesh in DefaultMeshes)
        {
            var vertexOffset = vertices.Count;
            vertices.AddRange(mesh.Vertices.Select(vertex => new Vertex(vertex.Position, Vector3.UnitZ, Vector2.Zero)));

            foreach (var triangle in mesh.Triangles)
            {
                triangles.Add(new Triangle(
                    vertexOffset + triangle.A,
                    vertexOffset + triangle.B,
                    vertexOffset + triangle.C));
            }
        }

        return new Mesh(vertices, [new Submesh(null, triangles)]);
    }

}
