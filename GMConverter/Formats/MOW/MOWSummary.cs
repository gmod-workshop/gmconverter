using System.Globalization;
using System.Text;

namespace GMConverter.Formats.MOW;

internal sealed record MOWSummary(
    string FilePath,
    string ModelPath,
    int BoneCount,
    int MeshCount,
    int AnimationCount,
    IReadOnlyList<string> MeshFiles,
    IReadOnlyList<string> MaterialFiles,
    IReadOnlyList<string> AnimationFiles)
{
    public static MOWSummary From(string inputPath, string modelPath, MOWModelFile model)
    {
        var boneCount = CountSkeletonBones(model.Root.FirstChild("Skeleton"));
        var meshFiles = model.Root
            .Descendants("VolumeView")
            .Concat(model.Root.Descendants("volumeview"))
            .Where(node => node.Values.Count > 0)
            .Select(node => node.Values[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var materialFiles = meshFiles
            .Select(meshFile => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(model.Path) ?? ".", meshFile))
            .Where(File.Exists)
            .SelectMany(meshPath => MOWPlyFile.Read(meshPath).Submeshes.Select(submesh => submesh.MaterialFile))
            .Where(materialFile => !string.IsNullOrWhiteSpace(materialFile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
        var animationFiles = model.Root
            .Descendants("sequence")
            .Select(GetAnimationFile)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => fileName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        return new MOWSummary(
            inputPath,
            modelPath,
            boneCount,
            meshFiles.Length,
            animationFiles.Length,
            meshFiles,
            materialFiles,
            animationFiles);
    }

    private static int CountSkeletonBones(MOWNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        return node.Children
            .Where(child => string.Equals(child.Name, "bone", StringComparison.OrdinalIgnoreCase))
            .Sum(child => 1 + CountSkeletonBones(child));
    }

    private static string? GetAnimationFile(MOWNode sequence)
    {
        var fileValues = sequence.FirstChild("file")?.Values;
        var file = fileValues is { Count: > 0 } ? fileValues[0] : null;
        if (!string.IsNullOrWhiteSpace(file))
        {
            return file;
        }

        return sequence.Values.Count > 0 && !string.IsNullOrWhiteSpace(sequence.Values[0])
            ? $"{sequence.Values[0]}.anm"
            : null;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {FilePath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Model: {ModelPath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Bones: {BoneCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Meshes: {MeshCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Animations: {AnimationCount}");

        if (MeshFiles.Count > 0)
        {
            builder.AppendLine("Mesh files: " + string.Join(", ", MeshFiles));
        }

        if (MaterialFiles.Count > 0)
        {
            builder.AppendLine("Material files: " + string.Join(", ", MaterialFiles));
        }

        if (AnimationFiles.Count > 0)
        {
            builder.AppendLine("Animation files: " + string.Join(", ", AnimationFiles));
        }

        return builder.ToString().TrimEnd();
    }
}
