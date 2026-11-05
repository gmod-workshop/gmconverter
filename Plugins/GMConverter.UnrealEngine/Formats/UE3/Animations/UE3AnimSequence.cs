using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Animations;

/// <summary>
/// A UE3 AnimSequence decoded into per-track keys, one track per entry of its AnimSet's
/// <c>TrackBoneNames</c>. <c>FrameCount</c> includes both the first and the last frame.
/// </summary>
internal sealed record UE3AnimSequence(string Name, int FrameCount, float Length, float RateScale, IReadOnlyList<UE3AnimTrack> Tracks)
{
    private const int _identity = -1;

    /// <summary>Frames per second, counting the gaps between the sequence's frames.</summary>
    public float FrameRate => FrameCount > 1 && Length > 0 ? (FrameCount - 1) / Length * (RateScale > 0 ? RateScale : 1) : 30f;

    public static UE3AnimSequence Read(UE3Package package, UE3PackageExport export, int trackCount)
    {
        var version = package.Summary.FileVersion;
        var (archive, properties) = package.OpenObject(export);
        var name = properties.GetName("SequenceName") ?? export.ObjectName;
        var frameCount = properties.GetInt("NumFrames") ?? 1;
        var length = properties.GetFloat("SequenceLength") ?? 0f;
        var rateScale = properties.GetFloat("RateScale") ?? 1f;
        var translationFormat = properties.GetName("TranslationCompressionFormat") ?? "ACF_None";
        var rotationFormat = properties.GetName("RotationCompressionFormat") ?? "ACF_None";
        var keyFormat = properties.GetName("KeyEncodingFormat") ?? "AKF_ConstantKeyLerp";
        var offsets = properties.GetIntArray("CompressedTrackOffsets");

        var raw = version >= 577 ? archive.ReadArray(item => ReadRawTrack(item, version)) : [];
        var streamLength = archive.ReadCount();
        var stream = archive.ReadBytes(streamLength).ToArray();

        List<UE3AnimTrack> tracks;
        if (offsets.Count == 0)
        {
            tracks = [.. raw];
        }
        else if (keyFormat == "AKF_PerTrackCompression")
        {
            tracks = DecodePerTrack(stream, offsets, trackCount, frameCount, archive.Names);
        }
        else
        {
            tracks = DecodeUniform(stream, offsets, trackCount, frameCount, translationFormat, rotationFormat, keyFormat == "AKF_VariableKeyLerp", version, archive.Names);
        }

        if (tracks.Count != trackCount)
        {
            throw new GMConverterException($"UE3 animation {name} has {tracks.Count} tracks; its AnimSet names {trackCount} bones.");
        }

        return new UE3AnimSequence(name, Math.Max(frameCount, 1), length, rateScale, tracks);
    }

    private static UE3AnimTrack ReadRawTrack(UE3Archive archive, int version)
    {
        var (_, positionCount, positionOffset) = archive.ReadBulkArray();
        var (_, rotationCount, rotationOffset) = archive.ReadBulkArray();
        if (version < 604)
        {
            archive.ReadBulkArray(); // key times
        }

        var reader = new UE3Archive(archive.Data, archive.Names, positionOffset);
        var positions = Enumerable.Range(0, positionCount).Select(_ => reader.ReadVector3()).ToArray();
        reader.Position = rotationOffset;
        var rotations = Enumerable.Range(0, rotationCount).Select(_ => reader.ReadQuaternion()).ToArray();
        return new UE3AnimTrack(positions, null, rotations, null);
    }

    private static List<UE3AnimTrack> DecodeUniform(
        byte[] stream,
        IReadOnlyList<int> offsets,
        int trackCount,
        int frameCount,
        string translationFormat,
        string rotationFormat,
        bool hasTimes,
        int version,
        IReadOnlyList<string> names)
    {
        List<UE3AnimTrack> tracks = new(trackCount);
        var reader = new UE3Archive(stream, names);
        for (var track = 0; track < trackCount && track * 4 + 3 < offsets.Count; track++)
        {
            var translationOffset = offsets[track * 4];
            var translationKeys = offsets[track * 4 + 1];
            var rotationOffset = offsets[track * 4 + 2];
            var rotationKeys = offsets[track * 4 + 3];

            List<Vector3> positions = [];
            float[]? positionTimes = null;
            if (translationKeys > 0)
            {
                reader.Position = translationOffset;
                var format = translationKeys == 1 ? "ACF_None" : translationFormat;
                Vector3 mins = default, ranges = default;
                if (format == "ACF_IntervalFixed32NoW")
                {
                    mins = reader.ReadVector3();
                    ranges = reader.ReadVector3();
                }

                for (var key = 0; key < translationKeys; key++)
                {
                    positions.Add(ReadPosition(reader, format, mins, ranges));
                }

                Align(reader);
                positionTimes = hasTimes ? ReadTimes(reader, translationKeys, frameCount) : null;
            }

            List<Quaternion> rotations = [];
            float[]? rotationTimes = null;
            if (rotationKeys > 0)
            {
                reader.Position = rotationOffset;
                var format = rotationKeys == 1 ? "ACF_Float96NoW" : rotationFormat;
                Vector3 mins = default, ranges = default;
                if (rotationKeys > 1 && (format == "ACF_IntervalFixed32NoW" || version < 761))
                {
                    mins = reader.ReadVector3();
                    ranges = reader.ReadVector3();
                }

                for (var key = 0; key < rotationKeys; key++)
                {
                    rotations.Add(ReadRotation(reader, format, mins, ranges));
                }

                Align(reader);
                rotationTimes = hasTimes ? ReadTimes(reader, rotationKeys, frameCount) : null;
            }

            tracks.Add(new UE3AnimTrack(positions, positionTimes, rotations, rotationTimes));
        }

        return tracks;
    }

    // Each track's translation and rotation carry their own format, component mask and key count.
    private static List<UE3AnimTrack> DecodePerTrack(byte[] stream, IReadOnlyList<int> offsets, int trackCount, int frameCount, IReadOnlyList<string> names)
    {
        List<UE3AnimTrack> tracks = new(trackCount);
        var reader = new UE3Archive(stream, names);
        for (var track = 0; track < trackCount && track * 2 + 1 < offsets.Count; track++)
        {
            List<Vector3> positions = [];
            float[]? positionTimes = null;
            if (offsets[track * 2] != _identity)
            {
                reader.Position = offsets[track * 2];
                var (format, mask, keys, hasTimes) = ReadPerTrackHeader(reader.ReadUInt32());
                var (mins, ranges) = format == "ACF_IntervalFixed32NoW" ? ReadMaskedRanges(reader, mask) : (default, default);
                for (var key = 0; key < keys; key++)
                {
                    positions.Add(format switch
                    {
                        "ACF_Fixed48NoW" => new Vector3(
                            (mask & 1) != 0 ? DecodeFixed48(reader.ReadUInt16()) : 0,
                            (mask & 2) != 0 ? DecodeFixed48(reader.ReadUInt16()) : 0,
                            (mask & 4) != 0 ? DecodeFixed48(reader.ReadUInt16()) : 0),
                        "ACF_Float96NoW" when (mask & 7) != 0 => new Vector3(
                            (mask & 1) != 0 ? reader.ReadSingle() : 0,
                            (mask & 2) != 0 ? reader.ReadSingle() : 0,
                            (mask & 4) != 0 ? reader.ReadSingle() : 0),
                        _ => ReadPosition(reader, format, mins, ranges),
                    });
                }

                Align(reader);
                positionTimes = hasTimes ? ReadTimes(reader, keys, frameCount) : null;
            }

            List<Quaternion> rotations = [];
            float[]? rotationTimes = null;
            if (offsets[track * 2 + 1] != _identity)
            {
                reader.Position = offsets[track * 2 + 1];
                var (format, mask, keys, hasTimes) = ReadPerTrackHeader(reader.ReadUInt32());
                var (mins, ranges) = format == "ACF_IntervalFixed32NoW" ? ReadMaskedRanges(reader, mask) : (default, default);
                for (var key = 0; key < keys; key++)
                {
                    if (format == "ACF_Fixed48NoW")
                    {
                        var x = (mask & 1) != 0 ? reader.ReadUInt16() : (ushort)32767;
                        var y = (mask & 2) != 0 ? reader.ReadUInt16() : (ushort)32767;
                        var z = (mask & 4) != 0 ? reader.ReadUInt16() : (ushort)32767;
                        rotations.Add(RestoreW((x - 32767) / 32767f, (y - 32767) / 32767f, (z - 32767) / 32767f));
                    }
                    else
                    {
                        rotations.Add(ReadRotation(reader, format, mins, ranges));
                    }
                }

                Align(reader);
                rotationTimes = hasTimes ? ReadTimes(reader, keys, frameCount) : null;
            }

            tracks.Add(new UE3AnimTrack(positions, positionTimes, rotations, rotationTimes));
        }

        return tracks;
    }

    private static (string Format, int Mask, int Keys, bool HasTimes) ReadPerTrackHeader(uint info)
    {
        var format = (int)(info >> 28) switch
        {
            0 => "ACF_None",
            1 => "ACF_Float96NoW",
            2 => "ACF_Fixed48NoW",
            3 => "ACF_IntervalFixed32NoW",
            4 => "ACF_Fixed32NoW",
            5 => "ACF_Float32NoW",
            6 => "ACF_Identity",
            var other => throw new GMConverterException($"UE3 animation uses unknown key format {other}."),
        };
        var mask = (int)((info >> 24) & 0xF);
        return (format, mask, (int)(info & 0xFFFFFF), (mask & 8) != 0);
    }

    private static (Vector3 Mins, Vector3 Ranges) ReadMaskedRanges(UE3Archive reader, int mask)
    {
        Vector3 mins = default, ranges = default;
        if ((mask & 1) != 0)
        {
            mins.X = reader.ReadSingle();
            ranges.X = reader.ReadSingle();
        }

        if ((mask & 2) != 0)
        {
            mins.Y = reader.ReadSingle();
            ranges.Y = reader.ReadSingle();
        }

        if ((mask & 4) != 0)
        {
            mins.Z = reader.ReadSingle();
            ranges.Z = reader.ReadSingle();
        }

        return (mins, ranges);
    }

    private static Vector3 ReadPosition(UE3Archive reader, string format, Vector3 mins, Vector3 ranges)
    {
        switch (format)
        {
            case "ACF_None":
            case "ACF_Float96NoW":
                return reader.ReadVector3();
            case "ACF_IntervalFixed32NoW":
                var packed = reader.ReadUInt32();
                return new Vector3(
                    ((packed & 0x3FF) / 511f - 1f) * ranges.X + mins.X,
                    (((packed >> 10) & 0x7FF) / 1023f - 1f) * ranges.Y + mins.Y,
                    ((packed >> 21) / 1023f - 1f) * ranges.Z + mins.Z);
            case "ACF_Fixed48NoW":
                const float scale = 128f / 32767f;
                return new Vector3((reader.ReadUInt16() - 32767) * scale, (reader.ReadUInt16() - 32767) * scale, (reader.ReadUInt16() - 32767) * scale);
            case "ACF_Identity":
                return Vector3.Zero;
            default:
                throw new GMConverterException($"UE3 animation translation format {format} is not supported.");
        }
    }

    private static Quaternion ReadRotation(UE3Archive reader, string format, Vector3 mins, Vector3 ranges)
    {
        switch (format)
        {
            case "ACF_None":
                return reader.ReadQuaternion();
            case "ACF_Float96NoW":
                return RestoreW(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            case "ACF_Fixed48NoW":
                return RestoreW((reader.ReadUInt16() - 32767) / 32767f, (reader.ReadUInt16() - 32767) / 32767f, (reader.ReadUInt16() - 32767) / 32767f);
            case "ACF_Fixed32NoW":
            case "ACF_IntervalFixed32NoW":
                var packed = reader.ReadUInt32();
                var x = (packed >> 21) / 1023f - 1f;
                var y = ((packed >> 10) & 0x7FF) / 1023f - 1f;
                var z = (packed & 0x3FF) / 511f - 1f;
                return format == "ACF_Fixed32NoW"
                    ? RestoreW(x, y, z)
                    : RestoreW(x * ranges.X + mins.X, y * ranges.Y + mins.Y, z * ranges.Z + mins.Z);
            case "ACF_Float32NoW":
                return DecodeFloat32NoW(reader.ReadUInt32());
            case "ACF_Identity":
                return Quaternion.Identity;
            default:
                throw new GMConverterException($"UE3 animation rotation format {format} is not supported.");
        }
    }

    // 11/11/10-bit floats with a 3-bit exponent biased to 123 and the sign in the top bit.
    private static Quaternion DecodeFloat32NoW(uint data)
    {
        static float Component(uint bits, int mantissaBits)
        {
            var exponent = (bits >> mantissaBits) & 7;
            var mantissa = bits & ((1u << mantissaBits) - 1);
            var sign = (bits >> (mantissaBits + 3)) & 1;
            var value = BitConverter.Int32BitsToSingle((int)(((exponent + 123) << 23) | (mantissa << (23 - mantissaBits))));
            return sign != 0 ? -value : value;
        }

        return RestoreW(Component(data >> 21, 7), Component((data >> 10) & 0x7FF, 7), Component(data & 0x3FF, 6));
    }

    private static float DecodeFixed48(ushort value) => value - 255f;

    private static Quaternion RestoreW(float x, float y, float z)
    {
        var w = 1f - (x * x + y * y + z * z);
        return new Quaternion(x, y, z, w > 0 ? MathF.Sqrt(w) : 0);
    }

    private static void Align(UE3Archive reader) => reader.Position = (reader.Position + 3) & ~3;

    private static float[] ReadTimes(UE3Archive reader, int keys, int frameCount)
    {
        if (keys <= 1)
        {
            return [0];
        }

        var times = new float[keys];
        for (var i = 0; i < keys; i++)
        {
            times[i] = frameCount < 256 ? reader.ReadByte() : reader.ReadUInt16();
        }

        Align(reader);
        return times;
    }
}
