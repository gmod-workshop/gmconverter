using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE2.Packages;

namespace GMConverter.UnrealEngine.Formats.UE2.Animations;

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
