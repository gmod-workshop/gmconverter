using System.Numerics;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Unreal;

internal sealed record UnrealMeshAnimation(
    IReadOnlyList<UnrealAnimationBone> Bones,
    IReadOnlyList<UnrealAnimationSequence> Sequences)
{
    private const int _maxAnimationCount = 100_000;
    private const int _maxBoneCount = 100_000;
    private const int _maxTrackCount = 1_000_000;
    private const int _maxKeyCount = 10_000_000;

    public static UnrealMeshAnimation Read(UnrealObjectReader reader)
    {
        reader.SkipProperties();
        _ = reader.ReadInt32();
        var bones = ReadLimitedArray(reader, ReadBone(reader), _maxBoneCount, "UE2 animation bone");

        if (reader.FileVersion >= 141)
        {
            try
            {
                return ReadRepublicCommando(reader, bones);
            }
            catch (GMConverterException)
            {
                throw;
            }
        }

        return ReadStandard(reader, bones);
    }

    private static UnrealMeshAnimation ReadRepublicCommando(
        UnrealObjectReader reader,
        List<UnrealAnimationBone> bones)
    {
        var sequences = ReadLimitedArray(
            reader,
            () =>
            {
                var sequenceInfo = ReadSequenceInfo(reader, isRepublicCommando: true);
                _ = reader.FileVersion < 143 ? reader.ReadInt32() : 0;
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                if (reader.FileVersion >= 143)
                {
                    _ = reader.ReadInt32();
                }

                var tracks = ReadLimitedArray(reader, ReadRepublicCommandoTrack(reader), _maxTrackCount, "UE2 SWRC animation track");
                return new UnrealAnimationSequence(
                    sequenceInfo.Name,
                    sequenceInfo.Group,
                    sequenceInfo.FrameCount,
                    sequenceInfo.Rate,
                    tracks);
            },
            _maxAnimationCount,
            "UE2 SWRC animation sequence");

        if (bones.Count == 0 || sequences.Count == 0)
        {
            throw new GMConverterException("UE2 animation did not contain bones and sequences.");
        }

        return new UnrealMeshAnimation(bones, sequences);
    }

    private static UnrealMeshAnimation ReadStandard(
        UnrealObjectReader reader,
        List<UnrealAnimationBone> bones)
    {
        var moves = ReadLimitedArray(reader, ReadMotionChunk(reader), _maxAnimationCount, "UE2 motion chunk");
        var sequenceInfos = ReadLimitedArray(reader, () => ReadSequenceInfo(reader, isRepublicCommando: false), _maxAnimationCount, "UE2 animation sequence");
        var sequences = sequenceInfos
            .Select((info, index) =>
            {
                var tracks = index < moves.Count ? moves[index].Tracks : [];
                return new UnrealAnimationSequence(info.Name, info.Group, info.FrameCount, info.Rate, tracks);
            })
            .ToArray();

        if (bones.Count == 0 || sequences.Length == 0)
        {
            throw new GMConverterException("UE2 animation did not contain bones and sequences.");
        }

        return new UnrealMeshAnimation(bones, sequences);
    }

    private static Func<UnrealAnimationBone> ReadBone(UnrealObjectReader reader)
    {
        return () => new UnrealAnimationBone(
            reader.ReadName(),
            reader.ReadUInt32(),
            reader.ReadInt32());
    }

    private static UnrealAnimationSequenceInfo ReadSequenceInfo(UnrealObjectReader reader, bool isRepublicCommando)
    {
        if (reader.FileVersion >= 115)
        {
            _ = reader.ReadSingle();
        }

        var name = reader.ReadName();
        var groups = ReadLimitedArray(reader, reader.ReadName, 1024, "UE2 animation group");
        _ = reader.ReadInt32();
        var frameCount = reader.ReadInt32();
        _ = ReadLimitedArray(reader, ReadAnimNotify(reader), 100_000, "UE2 animation notify");
        var rate = reader.ReadSingle();

        if (isRepublicCommando)
        {
            _ = reader.ReadInt32();
            _ = reader.ReadSingle();
            _ = reader.ReadByte();
            if (reader.FileVersion >= 144)
            {
                _ = reader.ReadSingle();
            }
        }

        return new UnrealAnimationSequenceInfo(
            string.IsNullOrWhiteSpace(name) ? "Anim" : name,
            groups.FirstOrDefault(group => !group.Equals("None", StringComparison.OrdinalIgnoreCase)) ?? "None",
            Math.Max(frameCount, 1),
            rate > 0.000001f ? rate : 30.0f);
    }

    private static Func<int> ReadAnimNotify(UnrealObjectReader reader)
    {
        return () =>
        {
            _ = reader.ReadSingle();
            _ = reader.ReadName();
            if (reader.FileVersion >= 112)
            {
                _ = reader.ReadObjectIndex();
            }

            return 0;
        };
    }

    private static Func<UnrealAnimationMotionChunk> ReadMotionChunk(UnrealObjectReader reader)
    {
        return () =>
        {
            _ = reader.ReadVector3();
            var trackTime = reader.ReadSingle();
            _ = reader.ReadInt32();
            _ = reader.ReadUInt32();
            _ = ReadLimitedArray(reader, reader.ReadInt32, _maxBoneCount, "UE2 motion bone index");
            var tracks = ReadLimitedArray(reader, ReadStandardTrack(reader), _maxTrackCount, "UE2 animation track");
            _ = ReadStandardTrack(reader)();
            return new UnrealAnimationMotionChunk(trackTime, tracks);
        };
    }

    private static Func<UnrealAnimationTrack> ReadStandardTrack(UnrealObjectReader reader)
    {
        return () =>
        {
            _ = reader.ReadUInt32();
            var rotations = ReadLimitedArray(reader, reader.ReadQuaternion, _maxKeyCount, "UE2 rotation key");
            var positions = ReadLimitedArray(reader, reader.ReadVector3, _maxKeyCount, "UE2 position key");
            var times = ReadLimitedArray(reader, reader.ReadSingle, _maxKeyCount, "UE2 time key");
            return new UnrealAnimationTrack(positions, rotations, times);
        };
    }

    private static Func<UnrealAnimationTrack> ReadRepublicCommandoTrack(UnrealObjectReader reader)
    {
        return () =>
        {
            var positionScale = reader.ReadSingle();
            var packedPositions = ReadLimitedArray(reader, () => ReadPackedVector(reader), _maxKeyCount, "UE2 SWRC position key");
            var packedRotations = ReadLimitedArray(reader, () => ReadPackedVector(reader), _maxKeyCount, "UE2 SWRC rotation key");
            var packedTimes = ReadLimitedArray(reader, reader.ReadByte, _maxKeyCount, "UE2 SWRC time key");

            List<float> times = new(packedTimes.Count);
            var time = 0;
            foreach (var packedTime in packedTimes)
            {
                times.Add(time);
                time += packedTime;
            }

            var positions = packedPositions
                .Select(position => position.ToVector(positionScale))
                .ToArray();
            var rotations = packedRotations
                .Select(rotation => TransformRepublicCommandoRotation(rotation, reader.FileVersion))
                .ToArray();

            return new UnrealAnimationTrack(positions, rotations, times);
        };
    }

    private static UnrealPackedShortVector ReadPackedVector(UnrealObjectReader reader)
    {
        return new UnrealPackedShortVector(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
    }

    private static Quaternion TransformRepublicCommandoRotation(UnrealPackedShortVector value, int fileVersion)
    {
        var rotation = fileVersion >= 151
            ? value.ToQuaternion()
            : value.ToOldQuaternion();

        rotation.X *= -1.0f;
        rotation.Y *= -1.0f;
        rotation.Z *= -1.0f;
        return Normalize(rotation);
    }

    private static List<T> ReadLimitedArray<T>(
        UnrealObjectReader reader,
        Func<T> readItem,
        int maxCount,
        string itemDescription)
    {
        var count = reader.ReadCompactIndex();
        if (count < 0 || count > maxCount)
        {
            throw new GMConverterException($"{itemDescription} array count is invalid: {count}");
        }

        var items = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(readItem());
        }

        return items;
    }

    private static Quaternion Normalize(Quaternion value)
    {
        return value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;
    }

    private sealed record UnrealAnimationMotionChunk(float TrackTime, IReadOnlyList<UnrealAnimationTrack> Tracks);

    private sealed record UnrealAnimationSequenceInfo(string Name, string Group, int FrameCount, float Rate);
}

internal sealed record UnrealAnimationBone(string Name, uint Flags, int ParentIndex);

internal sealed record UnrealAnimationSequence(
    string Name,
    string Group,
    int FrameCount,
    float Rate,
    IReadOnlyList<UnrealAnimationTrack> Tracks);

internal sealed record UnrealAnimationTrack(
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Quaternion> Rotations,
    IReadOnlyList<float> Times)
{
    public (Vector3 Position, Quaternion Rotation) Sample(int frameIndex, int frameCount)
    {
        var frame = (float)frameIndex;
        var position = Positions.Count == 0
            ? Vector3.Zero
            : SampleVector(Positions, Times, frame, frameCount);
        var rotation = Rotations.Count == 0
            ? Quaternion.Identity
            : SampleQuaternion(Rotations, Times, frame, frameCount);

        return (position, rotation);
    }

    private static Vector3 SampleVector(IReadOnlyList<Vector3> keys, IReadOnlyList<float> times, float frame, int frameCount)
    {
        if (keys.Count == 1 || frameCount <= 1 || frame <= 0.0f)
        {
            return keys[0];
        }

        if (times.Count > 0 && (keys.Count == 1 || keys.Count == times.Count))
        {
            var (previous, next, fraction) = GetTimedKeyParameters(times, frame, frameCount);
            if (keys.Count == 1)
            {
                return keys[0];
            }

            return fraction > 0.0f ? Vector3.Lerp(keys[previous], keys[next], fraction) : keys[previous];
        }

        var (evenPrevious, evenNext, evenFraction) = GetEvenKeyParameters(keys.Count, frame, frameCount);
        return evenFraction > 0.0f ? Vector3.Lerp(keys[evenPrevious], keys[evenNext], evenFraction) : keys[evenPrevious];
    }

    private static Quaternion SampleQuaternion(IReadOnlyList<Quaternion> keys, IReadOnlyList<float> times, float frame, int frameCount)
    {
        if (keys.Count == 1 || frameCount <= 1 || frame <= 0.0f)
        {
            return Normalize(keys[0]);
        }

        if (times.Count > 0 && (keys.Count == 1 || keys.Count == times.Count))
        {
            var (previous, next, fraction) = GetTimedKeyParameters(times, frame, frameCount);
            if (keys.Count == 1)
            {
                return Normalize(keys[0]);
            }

            return fraction > 0.0f ? Normalize(Quaternion.Slerp(keys[previous], keys[next], fraction)) : Normalize(keys[previous]);
        }

        var (evenPrevious, evenNext, evenFraction) = GetEvenKeyParameters(keys.Count, frame, frameCount);
        return evenFraction > 0.0f ? Normalize(Quaternion.Slerp(keys[evenPrevious], keys[evenNext], evenFraction)) : Normalize(keys[evenPrevious]);
    }

    private static (int Previous, int Next, float Fraction) GetTimedKeyParameters(
        IReadOnlyList<float> times,
        float frame,
        int frameCount)
    {
        var previous = FindTimeKey(times, frame);
        var next = previous + 1;
        if (next >= times.Count)
        {
            return (previous, times.Count - 1, 0.0f);
        }

        var duration = times[next] - times[previous];
        var fraction = Math.Abs(duration) > 0.000001f ? (frame - times[previous]) / duration : 0.0f;
        return (previous, next, Math.Clamp(fraction, 0.0f, 1.0f));
    }

    private static (int Previous, int Next, float Fraction) GetEvenKeyParameters(int keyCount, float frame, int frameCount)
    {
        var position = frame / frameCount * keyCount;
        var previous = Math.Clamp((int)MathF.Floor(position), 0, keyCount - 1);
        var fraction = position - previous;
        var next = previous + 1;
        if (next >= keyCount)
        {
            next = keyCount - 1;
            fraction = 0.0f;
        }

        return (previous, next, Math.Clamp(fraction, 0.0f, 1.0f));
    }

    private static int FindTimeKey(IReadOnlyList<float> times, float frame)
    {
        var low = 0;
        var high = times.Count - 1;
        while (low + 4 < high)
        {
            var middle = (low + high) / 2;
            if (frame < times[middle])
            {
                high = middle - 1;
            }
            else
            {
                low = middle;
            }
        }

        for (var i = low; i <= high; i++)
        {
            if (Math.Abs(frame - times[i]) < 0.000001f)
            {
                return i;
            }

            if (frame < times[i])
            {
                return i > 0 ? i - 1 : 0;
            }
        }

        return high;
    }

    private static Quaternion Normalize(Quaternion value)
    {
        return value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;
    }
}

internal readonly record struct UnrealPackedShortVector(short X, short Y, short Z)
{
    public Vector3 ToVector(float scale)
    {
        var factor = scale / 32767.0f;
        return new Vector3(X * factor, Y * factor, Z * factor);
    }

    public Quaternion ToOldQuaternion()
    {
        const float scale = 0.000095876726845745f;
        var x = X * scale;
        var y = Y * scale;
        var z = Z * scale;
        var length = MathF.Sqrt(x * x + y * y + z * z);
        if (length > 0.0f)
        {
            var factor = MathF.Sin(length / 2.0f) / length;
            x *= factor;
            y *= factor;
            z *= factor;
        }

        var wSquared = 1.0f - (x * x + y * y + z * z);
        return new Quaternion(x, y, z, wSquared > 0.0f ? MathF.Sqrt(wSquared) : 0.0f);
    }

    public Quaternion ToQuaternion()
    {
        const float scale = 0.70710678118f / 32767.0f;
        var a = unchecked((short)(X & 0xFFFE)) * scale;
        var b = unchecked((short)(Y & 0xFFFE)) * scale;
        var c = unchecked((short)(Z & 0xFFFE)) * scale;
        var dSquared = 1.0f - (a * a + b * b + c * c);
        var d = dSquared > 0.0f ? MathF.Sqrt(dSquared) : 0.0f;
        if ((Z & 1) != 0)
        {
            d = -d;
        }

        if ((Y & 1) != 0)
        {
            return (X & 1) != 0
                ? new Quaternion(d, a, b, c)
                : new Quaternion(c, d, a, b);
        }

        return (X & 1) != 0
            ? new Quaternion(b, c, d, a)
            : new Quaternion(a, b, c, d);
    }
}

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
        var outputPath = Path.Combine(outputDirectory, outputName + ".psa");

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
