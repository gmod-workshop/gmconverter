namespace GMConverter.UnrealEngine.Formats.UE2.Meshes;

internal sealed record UnrealMeshTriangle(int Wedge0, int Wedge1, int Wedge2, int MaterialIndex, int AuxMaterialIndex, uint SmoothingGroups);
