using System.Numerics;
using System.Text;
using GMConverter.UnrealEngine.Formats.UE2.Animations;
using GMConverter.UnrealEngine.Formats.UE2.Meshes;
using GMConverter.UnrealEngine.Formats.UE2.Packages;

namespace GMConverter.UnrealEngine.Formats.UE2.Exporters;

internal static class UnrealPsaExporter
{
    private static readonly Encoding _textEncoding = Encoding.UTF8;

    public static string Export(
        UnrealPackageFile package,
        UnrealPackageExport export,
        UnrealSkeletalMesh mesh,
        string outputDirectory)
    {
        using var stream = File.OpenRead(package.FilePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var archive = new UnrealObjectReader(package, reader, export);
        var animation = UnrealMeshAnimation.Read(archive);
        var outputName = package.GetFullExportName(export).Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? export.ObjectName;
        var outputPath = Path.Join(outputDirectory, outputName + ".psa");

        using var outputStream = File.Create(outputPath);
        using var writer = new BinaryWriter(outputStream, Encoding.UTF8, leaveOpen: false);
        WriteAnimation(writer, animation, mesh);
        return outputPath;
    }

    private static void WriteAnimation(BinaryWriter writer, UnrealMeshAnimation animation, UnrealSkeletalMesh mesh)
    {
        var bones = animation.Bones.Count > 0
            ? animation.Bones
            : mesh.Bones.Select(bone => new UnrealAnimationBone(bone.Name, bone.Flags, bone.ParentIndex)).ToArray();

        WriteChunkHeader(writer, "ANIMHEAD", 20100422, 0, 0);
        WriteChunkHeader(writer, "BONENAMES", 0, 120, bones.Count);
        for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            var bone = bones[boneIndex];
            var meshBone = mesh.Bones.FirstOrDefault(item => item.Name.Equals(bone.Name, StringComparison.OrdinalIgnoreCase));
            var childCount = meshBone is null
                ? 0
                : mesh.Bones.Count(item => item.ParentIndex == meshBone.Index && item.Index != meshBone.Index);
            WriteFixedString(writer, bone.Name, 64);
            writer.Write((int)bone.Flags);
            writer.Write(childCount);
            writer.Write(meshBone is not null ? meshBone.ParentIndex : bone.ParentIndex);
            WriteQuaternion(writer, Quaternion.Identity);
            WriteVector(writer, Vector3.Zero);
            writer.Write(1.0f);
            WriteVector(writer, Vector3.Zero);
        }

        var frameStartIndex = 0;
        WriteChunkHeader(writer, "ANIMINFO", 0, 168, animation.Sequences.Count);
        foreach (var sequence in animation.Sequences)
        {
            WriteFixedString(writer, sequence.Name, 64);
            WriteFixedString(writer, sequence.Group, 64);
            writer.Write(bones.Count);
            writer.Write(0);
            writer.Write(0);
            writer.Write(sequence.FrameCount * bones.Count);
            writer.Write(0.0f);
            writer.Write((float)sequence.FrameCount);
            writer.Write(sequence.Rate);
            writer.Write(0);
            writer.Write(frameStartIndex);
            writer.Write(sequence.FrameCount);
            frameStartIndex += sequence.FrameCount;
        }

        var keyCount = animation.Sequences.Sum(sequence => sequence.FrameCount * bones.Count);
        WriteChunkHeader(writer, "ANIMKEYS", 0, 32, keyCount);
        foreach (var sequence in animation.Sequences)
        {
            for (var frameIndex = 0; frameIndex < sequence.FrameCount; frameIndex++)
            {
                for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
                {
                    var track = boneIndex < sequence.Tracks.Count ? sequence.Tracks[boneIndex] : null;
                    var (position, rotation) = track?.Sample(frameIndex, sequence.FrameCount) ?? (Vector3.Zero, Quaternion.Identity);
                    WriteMirroredVector(writer, position);
                    WriteMirroredQuaternion(writer, rotation);
                    writer.Write(1.0f);
                }
            }
        }

        WriteChunkHeader(writer, "SCALEKEYS", 0, 16, 0);
    }

    private static void WriteChunkHeader(BinaryWriter writer, string name, int typeFlag, int dataSize, int dataCount)
    {
        WriteFixedString(writer, name, 20);
        writer.Write(typeFlag);
        writer.Write(dataSize);
        writer.Write(dataCount);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int byteLength)
    {
        var bytes = _textEncoding.GetBytes(value);
        var count = Math.Min(bytes.Length, byteLength - 1);
        writer.Write(bytes, 0, count);
        writer.Write(new byte[byteLength - count]);
    }

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    private static void WriteMirroredVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(-value.Y);
        writer.Write(value.Z);
    }

    private static void WriteMirroredQuaternion(BinaryWriter writer, Quaternion value)
    {
        var normalized = value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;
        writer.Write(normalized.X);
        writer.Write(-normalized.Y);
        writer.Write(normalized.Z);
        writer.Write(-normalized.W);
    }
}
