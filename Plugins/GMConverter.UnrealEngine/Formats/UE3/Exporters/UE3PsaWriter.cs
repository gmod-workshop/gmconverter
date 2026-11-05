using System.Numerics;
using System.Text;
using GMConverter.UnrealEngine.Formats.PSA;
using GMConverter.UnrealEngine.Formats.UE3.Animations;

namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>
/// Writes a UE3 AnimSet's sequences to one ActorX PSA, sampled at each sequence's frames, in the
/// same mirrored space as <see cref="UE3PskWriter"/>. Bones whose translation should come from the
/// mesh (rotation-only sets, or tracks with no translation keys) are listed in the clip sidecar.
/// </summary>
internal static class UE3PsaWriter
{
    public static void Write(string path, UE3AnimSet animSet, IReadOnlyList<UE3AnimSequence> sequences)
    {
        var bones = animSet.TrackBones;
        var translated = new HashSet<string>(animSet.TranslatedBones, StringComparer.OrdinalIgnoreCase);
        var meshTranslation = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackPositions = new Vector3?[bones.Count];
        for (var bone = 0; bone < bones.Count; bone++)
        {
            if (animSet.RotationOnly && !translated.Contains(bones[bone]))
            {
                meshTranslation.Add(bones[bone]);
            }

            fallbackPositions[bone] = sequences.Select(sequence => sequence.Tracks[bone].SamplePosition(0, sequence.FrameCount)).FirstOrDefault(position => position is not null);
            if (fallbackPositions[bone] is null)
            {
                meshTranslation.Add(bones[bone]);
            }
        }

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
        {
            WriteChunkHeader(writer, "ANIMHEAD", 20100422, 0, 0);
            WriteChunkHeader(writer, "BONENAMES", 0, 120, bones.Count);
            foreach (var bone in bones)
            {
                WriteFixedString(writer, bone, 64);
                writer.Write(0); // flags
                writer.Write(0); // children
                writer.Write(0); // parent
                writer.Write(new byte[16 + 12]);
                writer.Write(1f);
                writer.Write(new byte[12]);
            }

            WriteChunkHeader(writer, "ANIMINFO", 0, 168, sequences.Count);
            var firstFrame = 0;
            foreach (var sequence in sequences)
            {
                WriteFixedString(writer, sequence.Name, 64);
                WriteFixedString(writer, "None", 64);
                writer.Write(bones.Count);
                writer.Write(0); // root include
                writer.Write(0); // key compression style
                writer.Write(sequence.FrameCount * bones.Count);
                writer.Write(0f); // key reduction
                writer.Write((float)sequence.FrameCount);
                writer.Write(sequence.FrameRate);
                writer.Write(0); // start bone
                writer.Write(firstFrame);
                writer.Write(sequence.FrameCount);
                firstFrame += sequence.FrameCount;
            }

            WriteChunkHeader(writer, "ANIMKEYS", 0, 32, sequences.Sum(sequence => sequence.FrameCount) * bones.Count);
            foreach (var sequence in sequences)
            {
                for (var frame = 0; frame < sequence.FrameCount; frame++)
                {
                    for (var bone = 0; bone < bones.Count; bone++)
                    {
                        var track = sequence.Tracks[bone];
                        var position = track.SamplePosition(frame, sequence.FrameCount) ?? fallbackPositions[bone] ?? Vector3.Zero;
                        var rotation = track.SampleRotation(frame, sequence.FrameCount) ?? Quaternion.Identity;
                        writer.Write(position.X);
                        writer.Write(-position.Y);
                        writer.Write(position.Z);
                        writer.Write(rotation.X);
                        writer.Write(-rotation.Y);
                        writer.Write(rotation.Z);
                        writer.Write(-rotation.W);
                        writer.Write(1f);
                    }
                }
            }
        }

        if (meshTranslation.Count > 0)
        {
            new PSAClipSidecar(false, false, MeshTranslationBones: [.. bones.Where(meshTranslation.Contains)]).Write(path);
        }
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
        var bytes = Encoding.UTF8.GetBytes(value);
        var count = Math.Min(bytes.Length, byteLength - 1);
        writer.Write(bytes, 0, count);
        writer.Write(new byte[byteLength - count]);
    }
}
