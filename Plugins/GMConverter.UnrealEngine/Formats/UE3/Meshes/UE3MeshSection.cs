namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>A run of triangles drawn with one of the mesh's materials.</summary>
internal sealed record UE3MeshSection(int MaterialIndex, int FirstIndex, int TriangleCount);
