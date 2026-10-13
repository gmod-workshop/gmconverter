using GMConverter.SDK.Geometry;

namespace GMConverter.UI.Services;

internal sealed record PreviewSummary(
    int MeshCount,
    int SubmeshCount,
    int MaterialCount,
    int TextureCount,
    int VertexCount,
    int TriangleCount,
    int PhysicsPartCount)
{
    public static PreviewSummary From(Model model, int physicsPartCount)
    {
        return new PreviewSummary(
            model.Meshes.Count,
            model.Meshes.Sum(mesh => mesh.Submeshes.Count),
            model.Materials.Count,
            model.Textures.Count,
            model.Meshes.Sum(mesh => mesh.Vertices.Count),
            model.Meshes.Sum(mesh => mesh.Triangles.Count()),
            physicsPartCount);
    }

    public override string ToString()
    {
        return $"Meshes {MeshCount} | Submeshes {SubmeshCount} | Materials {MaterialCount} | Textures {TextureCount}\n" +
            $"Vertices {VertexCount} | Triangles {TriangleCount} | Physics {PhysicsPartCount}";
    }
}
