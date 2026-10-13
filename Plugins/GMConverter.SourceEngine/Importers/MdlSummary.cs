using System.Globalization;
using System.Text;
using MdlCrowbar;

namespace GMConverter.SourceEngine.Importers;

internal sealed record MdlSummary(
    string FilePath,
    string Name,
    string Id,
    int Version,
    bool HasMeshData,
    bool HasPhysicsMeshData,
    bool HasBoneAnimationData,
    bool HasVertexAnimationData,
    IReadOnlyList<string> TextureFolders,
    IReadOnlyList<string> TextureFiles)
{
    public static MdlSummary From(string inputPath, SourceModel sourceModel)
    {
        return new MdlSummary(
            inputPath,
            sourceModel.Name,
            sourceModel.ID,
            sourceModel.Version,
            sourceModel.HasMeshData,
            sourceModel.HasPhysicsMeshData,
            sourceModel.HasBoneAnimationData,
            sourceModel.HasVertexAnimationData,
            sourceModel.GetTextureFolders().ToArray(),
            sourceModel.GetTextureFileNames().ToArray());
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {FilePath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Name: {Name}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ID: {Id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Version: {Version}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Mesh data: {HasMeshData}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Physics mesh data: {HasPhysicsMeshData}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Bone animation data: {HasBoneAnimationData}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Vertex animation data: {HasVertexAnimationData}");

        if (TextureFolders.Count > 0)
        {
            builder.AppendLine("Texture folders: " + string.Join(", ", TextureFolders));
        }

        if (TextureFiles.Count > 0)
        {
            builder.AppendLine("Textures: " + string.Join(", ", TextureFiles));
        }

        return builder.ToString().TrimEnd();
    }
}
