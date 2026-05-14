using System.Numerics;
using System.Text;
using GMConverter.Common;

namespace GMConverter.Formats.Unreal;

internal static class UnrealActorXExporter
{
    public static UnrealActorXExportResult ExportMesh(
        UnrealPackageFile package,
        UnrealPackageExport export,
        string outputDirectory,
        string searchRoot)
    {
        Directory.CreateDirectory(outputDirectory);

        var className = package.GetClassName(export);
        var objectName = package.GetFullExportName(export).Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? export.ObjectName;
        var outputPath = Path.Combine(
            outputDirectory,
            objectName + (className.Equals("StaticMesh", StringComparison.OrdinalIgnoreCase) ? ".pskx" : ".psk"));

        using var stream = File.OpenRead(package.FilePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var archive = new UnrealObjectReader(package, reader, export);

        if (className.Equals("StaticMesh", StringComparison.OrdinalIgnoreCase))
        {
            var mesh = UnrealStaticMesh.Read(archive);
            var materialCount = Math.Max(mesh.Sections.Count, 1);
            var materials = UnrealMaterialExporter.ExportMaterials(
                package,
                mesh.MaterialReferences,
                materialCount,
                outputDirectory,
                searchRoot);
            WriteStaticMesh(outputPath, mesh, materials);
            return new UnrealActorXExportResult(outputPath);
        }

        if (className.Equals("SkeletalMesh", StringComparison.OrdinalIgnoreCase))
        {
            var mesh = UnrealSkeletalMesh.Read(archive);
            var materialCount = Math.Max(
                mesh.MaterialReferences.Count,
                mesh.Triangles.Count == 0 ? 1 : mesh.Triangles.Max(face => face.MaterialIndex) + 1);
            var materials = UnrealMaterialExporter.ExportMaterials(
                package,
                mesh.MaterialReferences,
                materialCount,
                outputDirectory,
                searchRoot);
            WriteSkeletalMesh(outputPath, mesh, materials);
            var animationPath = TryExportAnimation(package, export, mesh, outputDirectory);
            return new UnrealActorXExportResult(outputPath, animationPath);
        }

        throw new GMConverterException($"Unsupported UE2 mesh class: {className}");
    }

    private static string? TryExportAnimation(
        UnrealPackageFile package,
        UnrealPackageExport meshExport,
        UnrealSkeletalMesh mesh,
        string outputDirectory)
    {
        var animationExport = FindAnimationExport(package, meshExport, mesh);
        if (animationExport is null)
        {
            return null;
        }

        try
        {
            return UnrealPsaExporter.Export(package, animationExport, mesh, outputDirectory);
        }
        catch (GMConverterException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static UnrealPackageExport? FindAnimationExport(
        UnrealPackageFile package,
        UnrealPackageExport meshExport,
        UnrealSkeletalMesh mesh)
    {
        foreach (var reference in mesh.AnimationReferences.Where(reference => reference > 0))
        {
            var export = package.GetExport(reference);
            if (export is not null && IsAnimationClass(package.GetClassName(export)))
            {
                return export;
            }
        }

        var meshName = meshExport.ObjectName;
        var sameOuterAnimations = package.Exports
            .Where(export => export.SerialSize > 0 &&
                export.PackageIndex == meshExport.PackageIndex &&
                IsAnimationClass(package.GetClassName(export)))
            .ToArray();

        return sameOuterAnimations.FirstOrDefault(export => export.ObjectName.Equals(meshName + "Set", StringComparison.OrdinalIgnoreCase)) ??
            sameOuterAnimations.FirstOrDefault(export => export.ObjectName.Equals(meshName + "Anim", StringComparison.OrdinalIgnoreCase)) ??
            sameOuterAnimations.FirstOrDefault(export => export.ObjectName.Equals(meshName + "Anims", StringComparison.OrdinalIgnoreCase)) ??
            sameOuterAnimations.FirstOrDefault(export => export.ObjectName.StartsWith(meshName, StringComparison.OrdinalIgnoreCase)) ??
            (sameOuterAnimations.Length == 1 ? sameOuterAnimations[0] : null);
    }

    private static bool IsAnimationClass(string className)
    {
        return className.Equals("MeshAnimation", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("Animation", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteStaticMesh(string outputPath, UnrealStaticMesh mesh, IReadOnlyList<string> materials)
    {
        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        var vertices = mesh.Vertices;

        WriteChunkHeader(writer, "ACTRHEAD", 20100422, 0, 0);
        WriteChunkHeader(writer, "PNTS0000", 0, 12, vertices.Count);
        foreach (var vertex in vertices)
        {
            WriteMirroredVector(writer, vertex.Position);
        }

        WriteChunkHeader(writer, "VTXW0000", 0, 16, vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
        {
            writer.Write(i);
            var uv = mesh.GetUv(i);
            writer.Write(uv.X);
            writer.Write(uv.Y);
            writer.Write((byte)FindStaticMeshSection(mesh.Sections, i));
            writer.Write((byte)0);
            writer.Write((short)0);
        }

        WriteFaces(writer, mesh.Sections, mesh.Indices, forceFace32: true);
        WriteMaterials(writer, materials);
        WriteChunkHeader(writer, "REFSKELT", 0, 120, 0);
        WriteChunkHeader(writer, "RAWWEIGHTS", 0, 12, 0);

        if (vertices.Any(vertex => vertex.Normal != Vector3.Zero))
        {
            WriteChunkHeader(writer, "VTXNORMS", 0, 12, vertices.Count);
            foreach (var vertex in vertices)
            {
                WriteMirroredNormal(writer, vertex.Normal);
            }
        }
    }

    private static void WriteSkeletalMesh(string outputPath, UnrealSkeletalMesh mesh, IReadOnlyList<string> materials)
    {
        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

        WriteChunkHeader(writer, "ACTRHEAD", 20100422, 0, 0);
        WriteChunkHeader(writer, "PNTS0000", 0, 12, mesh.Points.Count);
        foreach (var point in mesh.Points)
        {
            WriteMirroredVector(writer, point);
        }

        WriteChunkHeader(writer, "VTXW0000", 0, 16, mesh.Wedges.Count);
        foreach (var wedge in mesh.Wedges)
        {
            writer.Write(wedge.PointIndex);
            writer.Write(wedge.U);
            writer.Write(wedge.V);
            writer.Write((byte)Math.Clamp(wedge.MaterialIndex, 0, 255));
            writer.Write((byte)0);
            writer.Write((short)0);
        }

        WriteTriangleFaces(writer, mesh.Triangles, mesh.Wedges.Count);
        WriteMaterials(writer, materials);

        WriteChunkHeader(writer, "REFSKELT", 0, 120, mesh.Bones.Count);
        foreach (var bone in mesh.Bones)
        {
            WriteFixedString(writer, bone.Name, 64);
            writer.Write(bone.Flags);
            writer.Write(mesh.Bones.Count(item => item.ParentIndex == bone.Index && item.Index != bone.Index));
            writer.Write(bone.ParentIndex);
            WriteMirroredQuaternion(writer, bone.Rotation);
            WriteMirroredVector(writer, bone.Position);
            writer.Write(bone.Length);
            WriteVector(writer, bone.Size);
        }

        WriteChunkHeader(writer, "RAWWEIGHTS", 0, 12, mesh.Influences.Count);
        foreach (var influence in mesh.Influences)
        {
            writer.Write(influence.Weight);
            writer.Write(influence.PointIndex);
            writer.Write(influence.BoneIndex);
        }
    }

    private static void WriteFaces(
        BinaryWriter writer,
        IReadOnlyList<UnrealMeshSection> sections,
        IReadOnlyList<int> indices,
        bool forceFace32)
    {
        var faceCount = sections.Sum(section => section.NumFaces);
        var useFace32 = forceFace32 || indices.Count > ushort.MaxValue;
        WriteChunkHeader(writer, useFace32 ? "FACE3200" : "FACE0000", 0, useFace32 ? 18 : 12, faceCount);

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            for (var faceIndex = 0; faceIndex < section.NumFaces; faceIndex++)
            {
                var indexOffset = section.FirstIndex + faceIndex * 3;
                if (indexOffset + 2 >= indices.Count)
                {
                    throw new GMConverterException("UE2 static mesh index buffer ended before all section faces were read.");
                }

                WriteFace(writer, indices[indexOffset + 1], indices[indexOffset], indices[indexOffset + 2], sectionIndex, useFace32);
            }
        }
    }

    private static void WriteTriangleFaces(BinaryWriter writer, IReadOnlyList<UnrealMeshTriangle> triangles, int wedgeCount)
    {
        var useFace32 = wedgeCount > ushort.MaxValue;
        WriteChunkHeader(writer, useFace32 ? "FACE3200" : "FACE0000", 0, useFace32 ? 18 : 12, triangles.Count);
        foreach (var triangle in triangles)
        {
            WriteFace(
                writer,
                triangle.Wedge0,
                triangle.Wedge1,
                triangle.Wedge2,
                triangle.MaterialIndex,
                useFace32);
        }
    }

    private static void WriteFace(BinaryWriter writer, int wedge0, int wedge1, int wedge2, int materialIndex, bool useFace32)
    {
        if (useFace32)
        {
            writer.Write(wedge1);
            writer.Write(wedge0);
            writer.Write(wedge2);
            writer.Write((byte)Math.Clamp(materialIndex, 0, 255));
            writer.Write((byte)0);
            writer.Write(1);
            return;
        }

        writer.Write((ushort)wedge1);
        writer.Write((ushort)wedge0);
        writer.Write((ushort)wedge2);
        writer.Write((byte)Math.Clamp(materialIndex, 0, 255));
        writer.Write((byte)0);
        writer.Write(1);
    }

    private static void WriteMaterials(BinaryWriter writer, IReadOnlyList<string> materials)
    {
        WriteChunkHeader(writer, "MATT0000", 0, 88, materials.Count);
        for (var i = 0; i < materials.Count; i++)
        {
            WriteFixedString(writer, materials[i], 64);
            writer.Write(i);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }
    }

    private static int FindStaticMeshSection(IReadOnlyList<UnrealMeshSection> sections, int vertexIndex)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (vertexIndex >= section.FirstVertex && vertexIndex <= section.LastVertex)
            {
                return i;
            }
        }

        return 0;
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

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteMirroredVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(-value.Y);
        writer.Write(value.Z);
    }

    private static void WriteMirroredNormal(BinaryWriter writer, Vector3 value)
    {
        var normal = Vector3.Normalize(value);
        writer.Write(normal.X);
        writer.Write(-normal.Y);
        writer.Write(normal.Z);
    }

    private static void WriteMirroredQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.X);
        writer.Write(-value.Y);
        writer.Write(value.Z);
        writer.Write(-value.W);
    }
}

internal sealed record UnrealActorXExportResult(string MeshPath, string? AnimationPath = null);
