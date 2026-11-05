namespace GMConverter.UnrealEngine.Formats.UE3.Meshes;

/// <summary>A run of a static mesh's triangles drawn with one material (a package index), as vertex index triples.</summary>
internal sealed record UE3StaticMeshSection(int Material, IReadOnlyList<int> Indices);
