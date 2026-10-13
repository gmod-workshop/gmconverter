namespace GMConverter.UnrealEngine.Formats.UE2.Meshes;

internal sealed record UnrealMeshSection(int FirstIndex, int FirstVertex, int LastVertex, int NumFaces, int MaterialIndex = 0);
