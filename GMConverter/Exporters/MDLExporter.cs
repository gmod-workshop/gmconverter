using System.Diagnostics;
using System.Numerics;
using System.Text;
using GMConverter.Common;
using GMConverter.Geometry;
using GMConverter.Source;

namespace GMConverter.Exporters;

/// <summary>
/// Exports a model to the Source Engine MDL format.
/// </summary>
internal sealed class MDLExporter : IExporter<MDLExportOptions>
{
    private static readonly UTF8Encoding _utf8NoBom = new(false);
    private const int _sourceMaxConvexPieces = 1024;

    public string OutputFormat => "mdl";

    public string OutputName => "Source Engine";

    public void Export(
        Model model,
        string outputDirectory,
        string baseName,
        MDLExportOptions options)
    {
        var sourceTools = SourceToolPaths.Resolve(options.StudioMdlPath, options.VtfCmdPath, options.BuildMaterials);
        var physicsOptions = options.Physics;
        var modelPath = options.ModelPath;
        var safeBaseName = NameHelpers.SanitizeFileName(baseName);
        var smdPath = Path.Combine(outputDirectory, $"{safeBaseName}.smd");
        var physicsSmdPath =
            physicsOptions is null ? null : Path.Combine(outputDirectory, $"{safeBaseName}_phys.smd");
        var animationSmdPaths = GetAnimationSmdPaths(model, outputDirectory, safeBaseName);
        var qcPath = Path.Combine(outputDirectory, $"{safeBaseName}.qc");
        var materialRoot = Path.Combine(outputDirectory, "materials");
        var materialRelativeDirectories = GetMaterialDirectories(model, modelPath);
        var materialRelativeDirectory = materialRelativeDirectories[0];
        var materialDirectory =
            Path.Combine(materialRoot, materialRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(materialDirectory);

        WriteSmd(model, smdPath);
        if (physicsSmdPath is not null)
        {
            WritePhysicsSmd(model, physicsSmdPath, physicsOptions!);
        }

        foreach (var (clip, animationSmdPath) in animationSmdPaths)
        {
            WriteAnimationSmd(model, clip, animationSmdPath);
        }

        WriteQc(qcPath, model, modelPath, safeBaseName, materialRelativeDirectories, physicsSmdPath, physicsOptions, animationSmdPaths);
        ExportSourceMaterials(model, materialDirectory, materialRelativeDirectory);

        var result = new MDLExportResult(qcPath, smdPath, physicsSmdPath, materialDirectory, materialRelativeDirectory);
        Compile(model, result, sourceTools, options.BuildMaterials);
    }

    private static void Compile(Model model, MDLExportResult result, SourceToolPaths sourceTools, bool buildMaterials)
    {
        if (buildMaterials)
        {
            if (sourceTools.CanCompileMaterials)
            {
                var materialCompiler = new SourceMaterialCompiler(sourceTools.VtfCmdPath!);
                materialCompiler.Compile(model.Materials, result.MaterialDirectory, result.MaterialRelativeDirectory);
            }
        }

        RunStudioMdl(sourceTools.StudioMdlPath, result.QcPath);
    }

    private static void RunStudioMdl(string studioMdlPath, string qcPath)
    {
        if (!File.Exists(studioMdlPath))
        {
            throw new GMConverterException($"studiomdl not found: {studioMdlPath}");
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = studioMdlPath,
            ArgumentList = { qcPath },
            UseShellExecute = false
        });

        process?.WaitForExit();

        if (process is null)
        {
            throw new GMConverterException("Failed to start studiomdl.");
        }

        if (process.ExitCode != 0)
        {
            throw new GMConverterException($"studiomdl exited with code {process.ExitCode}.");
        }
    }

    private static void WriteSmd(Model model, string smdPath)
    {
        using var writer = new StreamWriter(smdPath, false, _utf8NoBom);
        writer.WriteLine("version 1");
        WriteSmdNodes(writer, model.Skeleton);
        WriteReferenceSkeleton(writer, model.Skeleton);
        writer.WriteLine("triangles");

        foreach (var mesh in model.Meshes)
        {
            foreach (var submesh in mesh.Submeshes)
            {
                var materialName = submesh.MaterialName ?? "default";

                foreach (var triangle in submesh.Triangles)
                {
                    writer.WriteLine(materialName);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.A]);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.C]);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.B]);
                }
            }
        }

        writer.WriteLine("end");
    }

    private static void WriteSmdVertex(StreamWriter writer, Vertex vertex)
    {
        var position = vertex.Position;
        var normal = vertex.Normal;
        var uv = vertex.TextureCoordinate;
        var weights = NormalizeSmdWeights(vertex.BoneWeights);
        var parentBoneIndex = weights.Length == 0 ? 0 : weights[0].BoneIndex;
        var weightText = weights.Length == 0
            ? string.Empty
            : " " + weights.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
              string.Concat(weights.Select(weight => FormattableString.Invariant($" {weight.BoneIndex} {weight.Weight:0.######}")));

        writer.WriteLine(FormattableString.Invariant(
            $"{parentBoneIndex} {position.X:0.######} {position.Y:0.######} {position.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######} {uv.X:0.######} {uv.Y:0.######}{weightText}"));
    }

    private static VertexBoneWeight[] NormalizeSmdWeights(IReadOnlyList<VertexBoneWeight>? weights)
    {
        var validWeights = weights?
            .Where(weight => weight.BoneIndex >= 0 && weight.Weight > 0.0f)
            .GroupBy(weight => weight.BoneIndex)
            .Select(group => new VertexBoneWeight(group.Key, group.Sum(weight => weight.Weight)))
            .OrderByDescending(weight => weight.Weight)
            .ToArray();

        if (validWeights is not { Length: > 0 })
        {
            return [];
        }

        var totalWeight = validWeights.Sum(weight => weight.Weight);
        if (totalWeight <= 0.000001f)
        {
            return [];
        }

        return [.. validWeights.Select(weight => new VertexBoneWeight(weight.BoneIndex, weight.Weight / totalWeight))];
    }

    private static void WritePhysicsSmd(Model model, string physicsSmdPath, PhysicsOptions physicsOptions)
    {
        switch (physicsOptions.Mode)
        {
            case PhysicsMode.Bounds:
                WriteBoundsPhysicsSmd(model, physicsSmdPath);
                break;

            case PhysicsMode.Coacd:
                WriteCoacdPhysicsSmd(model, physicsSmdPath,
                    physicsOptions.Coacd ?? throw new GMConverterException("Missing CoACD options."));
                break;

            default:
                throw new GMConverterException($"Unsupported physics mode: {physicsOptions.Mode}");
        }
    }

    private static void WriteBoundsPhysicsSmd(Model model, string physicsSmdPath)
    {
        var bounds = model.Bounds().WithMinimumThickness();
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath);

        Vector3[] vertices =
        [
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z)
        ];

        WritePhysicsQuad(writer, vertices, 0, 3, 2, 1, -Vector3.UnitZ);
        WritePhysicsQuad(writer, vertices, 4, 5, 6, 7, Vector3.UnitZ);
        WritePhysicsQuad(writer, vertices, 0, 1, 5, 4, -Vector3.UnitY);
        WritePhysicsQuad(writer, vertices, 3, 7, 6, 2, Vector3.UnitY);
        WritePhysicsQuad(writer, vertices, 0, 4, 7, 3, -Vector3.UnitX);
        WritePhysicsQuad(writer, vertices, 1, 2, 6, 5, Vector3.UnitX);

        writer.WriteLine("end");
    }

    private static void WriteCoacdPhysicsSmd(Model model, string physicsSmdPath, CoacdOptions options)
    {
        var parts = CoacdNative.Decompose(
            model.Merge(),
            new CoacdDecompositionOptions(options.Threshold, options.MaxConvexPieces, options.MaxHullVertices));

        if (parts.Count == 0)
        {
            throw new GMConverterException("CoACD did not produce any convex parts.");
        }

        WritePhysicsPartsSmd(physicsSmdPath, parts);
    }

    private static void WritePhysicsPartsSmd(string physicsSmdPath, IReadOnlyList<Mesh> parts)
    {
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath);
        var triangleCount = 0;

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var materialName = FormattableString.Invariant($"physics_{partIndex}");
            var partNormal = GetPartNormal(part);

            foreach (var triangle in part.Triangles)
            {
                WritePhysicsTriangle(
                    writer,
                    part.Vertices[triangle.A].Position,
                    part.Vertices[triangle.B].Position,
                    part.Vertices[triangle.C].Position,
                    partNormal,
                    materialName);
                triangleCount++;
            }
        }

        if (triangleCount == 0)
        {
            throw new GMConverterException("CoACD produced convex parts, but none of them contained triangles.");
        }

        writer.WriteLine("end");
    }

    private static StreamWriter CreatePhysicsSmdWriter(string physicsSmdPath)
    {
        var writer = new StreamWriter(physicsSmdPath, false, _utf8NoBom);
        writer.WriteLine("version 1");
        WriteSmdNodes(writer, null);
        WriteReferenceSkeleton(writer, null);
        writer.WriteLine("triangles");
        return writer;
    }

    private static Vector3 GetPartNormal(Mesh part)
    {
        foreach (var triangle in part.Triangles)
        {
            var normal = triangle.Normal(part.Vertices);

            if (normal != Vector3.UnitZ)
            {
                return normal;
            }
        }

        return Vector3.UnitZ;
    }

    private static void WritePhysicsQuad(StreamWriter writer, Vector3[] vertices, int a, int b, int c,
        int d, Vector3 normal)
    {
        WritePhysicsTriangle(writer, vertices[a], vertices[b], vertices[c], normal);
        WritePhysicsTriangle(writer, vertices[a], vertices[c], vertices[d], normal);
    }

    private static void WritePhysicsTriangle(StreamWriter writer, Vector3 a, Vector3 b, Vector3 c,
        Vector3 normal)
    {
        WritePhysicsTriangle(writer, a, b, c, normal, "physics");
    }

    private static void WritePhysicsTriangle(StreamWriter writer, Vector3 a, Vector3 b, Vector3 c,
        Vector3 normal, string materialName)
    {
        writer.WriteLine(materialName);
        WritePhysicsVertex(writer, a, normal);
        WritePhysicsVertex(writer, b, normal);
        WritePhysicsVertex(writer, c, normal);
    }

    private static void WritePhysicsVertex(StreamWriter writer, Vector3 vertex, Vector3 normal)
    {
        writer.WriteLine(FormattableString.Invariant(
            $"0 {vertex.X:0.######} {vertex.Y:0.######} {vertex.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######} 0 0"));
    }

    private static void WriteQc(
        string qcPath,
        Model model,
        string modelPath,
        string safeBaseName,
        IReadOnlyList<string> materialRelativeDirectories,
        string? physicsSmdPath,
        PhysicsOptions? physicsOptions,
        (AnimationClip Clip, string SmdPath)[] animationSmdPaths)
    {
        var smdFileName = $"{safeBaseName}.smd";
        using var writer = new StreamWriter(qcPath, false, _utf8NoBom);

        writer.WriteLine(FormattableString.Invariant($"$modelname \"{modelPath.Replace('\\', '/')}\""));
        writer.WriteLine(FormattableString.Invariant($"$body \"body\" \"{smdFileName}\""));
        foreach (var materialRelativeDirectory in materialRelativeDirectories)
        {
            writer.WriteLine(FormattableString.Invariant($"$cdmaterials \"{materialRelativeDirectory.Replace('\\', '/')}\""));
        }

        if (model.Skeleton is null && animationSmdPaths.Length == 0)
        {
            writer.WriteLine("$staticprop");
        }

        writer.WriteLine("$surfaceprop \"metal\"");

        writer.WriteLine("$sequence \"idle\" \"{0}\" fps 1", smdFileName);

        if (animationSmdPaths.Length > 0)
        {
            foreach (var (clip, animationSmdPath) in animationSmdPaths)
            {
                writer.WriteLine(FormattableString.Invariant(
                    $"$sequence \"{EscapeQcString(clip.Name)}\" \"{Path.GetFileName(animationSmdPath)}\" fps {clip.FrameRate:0.######}"));
            }
        }

        if (physicsSmdPath is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"$collisionmodel \"{Path.GetFileName(physicsSmdPath)}\""));
            writer.WriteLine("{");
            if (physicsOptions?.Mode is PhysicsMode.Coacd)
            {
                writer.WriteLine("    $concave");
                writer.WriteLine(FormattableString.Invariant($"    $maxconvexpieces {_sourceMaxConvexPieces}"));
            }

            writer.WriteLine(FormattableString.Invariant($"    $mass {(physicsOptions?.Mass ?? 100.0f):0.###}"));
            writer.WriteLine("}");
        }
    }

    private static (AnimationClip Clip, string SmdPath)[] GetAnimationSmdPaths(
        Model model,
        string outputDirectory,
        string safeBaseName)
    {
        if (model.Skeleton is null || model.Animations is not { Count: > 0 })
        {
            return [];
        }

        return
        [
            .. model.Animations
            .Where(clip => clip.Tracks.OfType<BoneTransformTrack>().Any())
            .Select((clip, index) => (
                clip,
                Path.Combine(outputDirectory, $"{safeBaseName}_{index}_{NameHelpers.SanitizeFileName(clip.Name)}.smd")))
        ];
    }

    private static void WriteAnimationSmd(Model model, AnimationClip clip, string smdPath)
    {
        if (model.Skeleton is null)
        {
            return;
        }

        using var writer = new StreamWriter(smdPath, false, _utf8NoBom);
        writer.WriteLine("version 1");
        WriteSmdNodes(writer, model.Skeleton);
        writer.WriteLine("skeleton");

        var tracksByBone = clip.Tracks
            .OfType<BoneTransformTrack>()
            .GroupBy(track => track.BoneIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var lastFrame = GetLastAnimationFrame(clip, tracksByBone.Values);

        for (var frameIndex = 0; frameIndex <= lastFrame; frameIndex++)
        {
            writer.WriteLine(FormattableString.Invariant($"time {frameIndex}"));
            var frameTransforms = model.Skeleton.Bones.ToDictionary(
                bone => bone.Index,
                bone => GetFrameTransform(bone, tracksByBone.GetValueOrDefault(bone.Index), frameIndex, clip.FrameRate));
            var sourceTransforms = BuildSourceLocalTransforms(model.Skeleton, frameTransforms);

            foreach (var bone in model.Skeleton.Bones.OrderBy(bone => bone.Index))
            {
                WriteSmdBoneTransform(writer, bone.Index, sourceTransforms[bone.Index]);
            }
        }

        writer.WriteLine("end");
    }

    private static int GetLastAnimationFrame(AnimationClip clip, IEnumerable<BoneTransformTrack> tracks)
    {
        var maxFrame = tracks
            .SelectMany(track => track.Keyframes)
            .Select(keyframe => ToAnimationFrame(keyframe.TimeSeconds, clip.FrameRate))
            .DefaultIfEmpty(0)
            .Max();
        var durationFrame = ToAnimationFrame(clip.DurationSeconds, clip.FrameRate);

        return Math.Max(0, Math.Max(maxFrame, durationFrame));
    }

    private static Transform GetFrameTransform(Bone bone, BoneTransformTrack? track, int frameIndex, float frameRate)
    {
        if (track is null || track.Keyframes.Count == 0)
        {
            return bone.LocalBindPose;
        }

        Transform transform = bone.LocalBindPose;
        foreach (var keyframe in track.Keyframes.OrderBy(keyframe => keyframe.TimeSeconds))
        {
            if (ToAnimationFrame(keyframe.TimeSeconds, frameRate) > frameIndex)
            {
                break;
            }

            transform = keyframe.Transform;
        }

        return transform;
    }

    private static int ToAnimationFrame(float timeSeconds, float frameRate)
    {
        return (int)MathF.Round(timeSeconds * frameRate);
    }

    private static void WriteSmdNodes(StreamWriter writer, Skeleton? skeleton)
    {
        writer.WriteLine("nodes");

        if (skeleton is null || skeleton.Bones.Count == 0)
        {
            writer.WriteLine("0 \"root\" -1");
        }
        else
        {
            foreach (var bone in skeleton.Bones.OrderBy(bone => bone.Index))
            {
                writer.WriteLine(FormattableString.Invariant(
                    $"{bone.Index} \"{EscapeSmdString(bone.Name)}\" {bone.ParentIndex}"));
            }
        }

        writer.WriteLine("end");
    }

    private static void WriteReferenceSkeleton(StreamWriter writer, Skeleton? skeleton)
    {
        writer.WriteLine("skeleton");
        writer.WriteLine("time 0");

        if (skeleton is null || skeleton.Bones.Count == 0)
        {
            writer.WriteLine("0 0 0 0 0 0 0");
        }
        else
        {
            var sourceTransforms = BuildSourceLocalTransforms(skeleton);
            foreach (var bone in skeleton.Bones.OrderBy(bone => bone.Index))
            {
                WriteSmdBoneTransform(writer, bone.Index, sourceTransforms[bone.Index]);
            }
        }

        writer.WriteLine("end");
    }

    private static Dictionary<int, Transform> BuildSourceLocalTransforms(
        Skeleton skeleton,
        Dictionary<int, Transform>? localTransforms = null)
    {
        Dictionary<int, Matrix4x4> originalWorldTransforms = [];
        Dictionary<int, Matrix4x4> sourceWorldTransforms = [];
        Dictionary<int, Transform> sourceLocalTransforms = [];

        foreach (var bone in skeleton.Bones.OrderBy(bone => bone.Index))
        {
            var localTransform = localTransforms is not null && localTransforms.TryGetValue(bone.Index, out var overrideTransform)
                ? overrideTransform
                : bone.LocalBindPose;
            var parentOriginalWorld = bone.ParentIndex >= 0 && originalWorldTransforms.TryGetValue(bone.ParentIndex, out var originalParent)
                ? originalParent
                : Matrix4x4.Identity;
            var parentSourceWorld = bone.ParentIndex >= 0 && sourceWorldTransforms.TryGetValue(bone.ParentIndex, out var sourceParent)
                ? sourceParent
                : Matrix4x4.Identity;
            var originalWorld = ToTransformMatrix(localTransform, includeScale: true) * parentOriginalWorld;

            Matrix4x4.Invert(parentSourceWorld, out var inverseParentSourceWorld);
            var sourceTranslation = Vector3.Transform(originalWorld.Translation, inverseParentSourceWorld);
            var sourceLocalTransform = new Transform(sourceTranslation, localTransform.Rotation, Vector3.One);
            var sourceWorld = ToTransformMatrix(sourceLocalTransform, includeScale: false) * parentSourceWorld;

            originalWorldTransforms[bone.Index] = originalWorld;
            sourceLocalTransforms[bone.Index] = sourceLocalTransform;
            sourceWorldTransforms[bone.Index] = sourceWorld;
        }

        return sourceLocalTransforms;
    }

    private static Matrix4x4 ToTransformMatrix(Transform transform, bool includeScale)
    {
        var matrix = Matrix4x4.CreateFromQuaternion(NormalizeQuaternion(transform.Rotation)) *
            Matrix4x4.CreateTranslation(transform.Translation);

        return includeScale
            ? Matrix4x4.CreateScale(transform.Scale) * matrix
            : matrix;
    }

    private static void WriteSmdBoneTransform(StreamWriter writer, int boneIndex, Transform transform)
    {
        var rotation = ToEulerRadians(transform.Rotation);
        var translation = transform.Translation;
        writer.WriteLine(FormattableString.Invariant(
            $"{boneIndex} {translation.X:0.######} {translation.Y:0.######} {translation.Z:0.######} {rotation.X:0.######} {rotation.Y:0.######} {rotation.Z:0.######}"));
    }

    private static Vector3 ToEulerRadians(Quaternion rotation)
    {
        rotation = NormalizeQuaternion(rotation);

        var sinrCosp = 2.0 * (rotation.W * rotation.X + rotation.Y * rotation.Z);
        var cosrCosp = 1.0 - 2.0 * (rotation.X * rotation.X + rotation.Y * rotation.Y);
        var x = Math.Atan2(sinrCosp, cosrCosp);

        var sinp = 2.0 * (rotation.W * rotation.Y - rotation.Z * rotation.X);
        var y = Math.Abs(sinp) >= 1.0
            ? Math.CopySign(Math.PI / 2.0, sinp)
            : Math.Asin(sinp);

        var sinyCosp = 2.0 * (rotation.W * rotation.Z + rotation.X * rotation.Y);
        var cosyCosp = 1.0 - 2.0 * (rotation.Y * rotation.Y + rotation.Z * rotation.Z);
        var z = Math.Atan2(sinyCosp, cosyCosp);

        return new Vector3((float)x, (float)y, (float)z);
    }

    private static Quaternion NormalizeQuaternion(Quaternion rotation)
    {
        return rotation.LengthSquared() <= 0.000001f ? Quaternion.Identity : Quaternion.Normalize(rotation);
    }

    private static string EscapeSmdString(string value)
    {
        return value.Replace("\"", "'", StringComparison.Ordinal);
    }

    private static string EscapeQcString(string value)
    {
        return value.Replace("\"", "'", StringComparison.Ordinal);
    }

    private static void ExportSourceMaterials(Model model, string materialDirectory, string materialRelativeDirectory)
    {
        foreach (var material in model.Materials)
        {
            if (material.DiffuseTexture is null)
            {
                continue;
            }

            var pngPath = Path.Combine(materialDirectory, $"{material.Name}.png");
            var vmtPath = Path.Combine(materialDirectory, $"{material.Name}.vmt");
            var sourceTexturePath = $"{materialRelativeDirectory}/{material.Name}".Replace('\\', '/');

            if (UseSourcePhong(material))
            {
                material.DiffuseTexture.WritePng(pngPath, material.SpecularTexture!);
            }
            else
            {
                material.DiffuseTexture.WritePng(pngPath);
            }

            material.NormalTexture?.WritePng(Path.Combine(materialDirectory, $"{material.Name}_normal.png"));
            material.SpecularTexture?.WritePng(Path.Combine(materialDirectory, $"{material.Name}_spec.png"));

            using var writer = new StreamWriter(vmtPath, false, _utf8NoBom);
            writer.WriteLine("\"VertexLitGeneric\"");
            writer.WriteLine("{");
            writer.WriteLine(FormattableString.Invariant($"    \"$basetexture\" \"{sourceTexturePath}\""));
            writer.WriteLine("    \"$nocull\" \"1\"");
            WriteSurfaceProp(writer, material);

            if (material.NormalTexture is not null)
            {
                writer.WriteLine(FormattableString.Invariant($"    \"$bumpmap\" \"{sourceTexturePath}_normal\""));
            }

            if (UseSourcePhong(material))
            {
                WritePhongParameters(writer, $"{sourceTexturePath}_spec", material);
            }
            else if (material.HasAlpha)
            {
                writer.WriteLine("    \"$translucent\" \"1\"");
            }

            if (material.IsIlluminated)
            {
                writer.WriteLine("    \"$selfillum\" \"1\"");
                writer.WriteLine(FormattableString.Invariant($"    \"$selfillummask\" \"{sourceTexturePath}_illum\""));
            }

            writer.WriteLine("}");

            material.EmissiveTexture?.WritePng(Path.Combine(materialDirectory, $"{material.Name}_illum.png"));
        }
    }

    private static void WriteSurfaceProp(StreamWriter writer, Material material)
    {
        var surfaceProp = SourceMaterialSurfaceProps.For(material);
        if (surfaceProp is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"    \"$surfaceprop\" \"{surfaceProp}\""));
        }
    }

    private static bool UseSourcePhong(Material material)
    {
        return material.DiffuseTexture is not null &&
            material.SpecularTexture is not null &&
            !material.HasAlpha;
    }

    private static void WritePhongParameters(StreamWriter writer, string specularTexturePath, Material material)
    {
        var settings = SourcePhongSettings.For(material);

        writer.WriteLine("    \"$phong\" \"1\"");
        writer.WriteLine("    \"$basemapalphaphongmask\" \"1\"");
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponenttexture\" \"{specularTexturePath}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongboost\" \"{settings.Boost}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongexponent\" \"{settings.Exponent}\""));
        writer.WriteLine(FormattableString.Invariant($"    \"$phongfresnelranges\" \"{settings.FresnelRanges}\""));
    }

    private static string[] GetMaterialDirectories(Model model, string modelPath)
    {
        var directories = model.Materials
            .SelectMany(MaterialPaths)
            .Select(NormalizeMaterialDirectory)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return directories.Length == 0 ? [GetMaterialDirectory(modelPath)] : directories;
    }

    private static IEnumerable<string> MaterialPaths(Material material)
    {
        if (!string.IsNullOrWhiteSpace(material.Path))
        {
            yield return material.Path;
        }

        foreach (var texture in material.Textures)
        {
            if (!string.IsNullOrWhiteSpace(texture.Path))
            {
                yield return texture.Path;
            }
        }
    }

    private static string GetMaterialDirectory(string modelPath)
    {
        var normalized = modelPath.Replace('\\', '/');

        if (normalized.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');

        return string.IsNullOrWhiteSpace(directory) ? "models/gmconverter" : directory;
    }

    private static string NormalizeMaterialDirectory(string materialDirectory)
    {
        var normalized = materialDirectory.Replace('\\', '/').Trim('/');

        if (normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["materials/".Length..];
        }

        return normalized;
    }

    private sealed record MDLExportResult(
        string QcPath,
        string SmdPath,
        string? PhysicsSmdPath,
        string MaterialDirectory,
        string MaterialRelativeDirectory);
}

internal sealed record MDLExportOptions(
    string ModelPath,
    string? StudioMdlPath,
    string? VtfCmdPath,
    bool BuildMaterials,
    PhysicsOptions? Physics);
