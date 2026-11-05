using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.UnrealEngine.Formats.PSA;
using GMConverter.UnrealEngine.Formats.PSK;
using GMConverter.UnrealEngine.Formats.UE3.Animations;
using GMConverter.UnrealEngine.Formats.UE3.Meshes;
using GMConverter.UnrealEngine.Formats.UE3.Packages;

namespace GMConverter.UnrealEngine.Formats.UE3.Exporters;

/// <summary>Exports UE3 skeletal meshes and anim sets as PSK/PSA files with material and socket sidecars.</summary>
internal static class UE3MeshExporter
{
    private const int _maximumTextureSize = 4096;

    /// <summary>
    /// Writes the mesh, its materials and sockets, plus a PSA for each AnimSet in the same package
    /// that animates only bones the mesh has.
    /// </summary>
    public static UE3ExportResult ExportMesh(UE3GameContent content, UE3Package package, UE3PackageExport export, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var mesh = UE3SkeletalMesh.Read(package, export);
        var slots = UE3MaterialExporter.Export(content, package, mesh.Materials, outputDirectory, _maximumTextureSize);
        var meshPath = Path.Combine(outputDirectory, mesh.Name + ".psk");
        UE3PskWriter.Write(meshPath, mesh, [.. slots.Select(slot => slot.Name)], [.. slots.Select(slot => (slot.AddressX, slot.AddressY))]);
        WriteSockets(mesh, meshPath);
        WriteModel(mesh, meshPath, firstPerson: false);

        var boneNames = new HashSet<string>(mesh.Bones.Select(bone => bone.Name), StringComparer.OrdinalIgnoreCase);
        List<string> animations = [];
        foreach (var setExport in package.Exports.Where(item => package.GetClassName(item) == "AnimSet"))
        {
            var animSet = UE3AnimSet.Read(package, setExport);
            if (animSet.TrackBones.Count > 0 && animSet.TrackBones.All(boneNames.Contains) &&
                TryExportAnimSet(content, package, setExport, animSet, outputDirectory) is { } path)
            {
                animations.Add(path);
            }
        }

        return new UE3ExportResult(meshPath, animations);
    }

    /// <summary>Writes one AnimSet's sequences to a PSA.</summary>
    public static UE3ExportResult ExportAnimSet(UE3GameContent content, UE3Package package, UE3PackageExport export, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var animSet = UE3AnimSet.Read(package, export);
        var path = TryExportAnimSet(content, package, export, animSet, outputDirectory)
            ?? throw new GMConverterException($"UE3 AnimSet {export.ObjectName} has no sequences that could be read.");
        return new UE3ExportResult(null, [path]);
    }

    private static string? TryExportAnimSet(UE3GameContent content, UE3Package package, UE3PackageExport export, UE3AnimSet animSet, string outputDirectory)
    {
        List<UE3AnimSequence> sequences = [];
        foreach (var index in animSet.Sequences)
        {
            if (package.GetExport(index) is not { } sequenceExport)
            {
                continue;
            }

            try
            {
                sequences.Add(UE3AnimSequence.Read(package, sequenceExport, animSet.TrackBones.Count));
            }
            catch (GMConverterException)
            {
                // One unreadable sequence shouldn't cost the set its others.
            }
        }

        if (sequences.Count == 0)
        {
            return null;
        }

        var path = Path.Combine(outputDirectory, export.ObjectName + ".psa");
        var events = new UE3AnimationEvents(content);
        UE3PsaWriter.Write(path, animSet, sequences, sequences
            .Select(sequence => (sequence.Name, Events: events.Read(package, sequence)))
            .Where(pair => pair.Events.Count > 0)
            .DistinctBy(pair => pair.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Name, pair => (IReadOnlyList<PSAClipEvent>)pair.Events, StringComparer.OrdinalIgnoreCase));
        return path;
    }

    /// <summary>Records the mesh's facing (in the PSK's mirrored space) and whether it is a view model.</summary>
    public static void WriteModel(UE3SkeletalMesh mesh, string meshPath, bool firstPerson)
    {
        Vector3? forward = mesh.Forward is { } facing ? new Vector3(facing.X, -facing.Y, facing.Z) : null;
        new PSKModelSidecar(forward, firstPerson).Write(meshPath);
    }

    public static void WriteSockets(UE3SkeletalMesh mesh, string meshPath)
    {
        if (mesh.Sockets.Count == 0)
        {
            return;
        }

        new PSKSocketSidecar([.. mesh.Sockets.Select(socket =>
        {
            var rotation = ToQuaternion(socket.Rotation);
            return new PSKSocket(
                socket.Name,
                socket.BoneName,
                new Vector3(socket.Location.X, -socket.Location.Y, socket.Location.Z),
                new Quaternion(-rotation.X, rotation.Y, -rotation.Z, rotation.W));
        })]).Write(meshPath);
    }

    // Unreal's FRotator::Quaternion, from 16-bit angle units.
    private static Quaternion ToQuaternion((int Pitch, int Yaw, int Roll) rotator)
    {
        const float unitsToHalfRadians = MathF.PI / 65536f;
        var (sp, cp) = MathF.SinCos(rotator.Pitch * unitsToHalfRadians);
        var (sy, cy) = MathF.SinCos(rotator.Yaw * unitsToHalfRadians);
        var (sr, cr) = MathF.SinCos(rotator.Roll * unitsToHalfRadians);
        return new Quaternion(
            cr * sp * sy - sr * cp * cy,
            -cr * sp * cy - sr * cp * sy,
            cr * cp * sy - sr * sp * cy,
            cr * cp * cy + sr * sp * sy);
    }
}
