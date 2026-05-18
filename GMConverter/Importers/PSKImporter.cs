using System.Numerics;
using System.Text.Json;
using GMConverter.Common;
using GMConverter.Formats.PSA;
using GMConverter.Formats.PSK;
using GMConverter.Geometry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GMConverter.Importers;

internal sealed class PSKImporter : IImporter
{
    public string InputFormat => "psk";

    public string InputName => "Unreal Engine";

    public object Summarize(string inputPath)
    {
        if (IsSceneManifest(inputPath))
        {
            var manifest = ReadSceneManifest(inputPath);
            return $"Unreal scene with {manifest.Entries.Count} mesh part(s).";
        }

        return PSKSummary.From(inputPath, PSKFile.Read(inputPath));
    }

    public Model Parse(string inputPath, ModelParseOptions options)
    {
        return IsSceneManifest(inputPath) ? ParseScene(inputPath, options) : ParseSingle(inputPath, options, null);
    }

    private static Model ParseScene(string inputPath, ModelParseOptions options)
    {
        var manifest = ReadSceneManifest(inputPath);
        // UE/Fortnite source assets are authored in centimeters, but glTF/standard mesh formats use
        // meters — so a scene that imports with default ScaleFactor=1.0 lands 100× too large. Fold
        // a cm→m factor into ScaleFactor for UE scenes specifically, on top of any user override.
        var sceneOptions = options with { ScaleFactor = options.ScaleFactor * 0.01f };
        var materialResolver = PSKMaterialResolver.Create(sceneOptions.Materials);
        List<Mesh> meshes = [];
        Dictionary<string, Material> materials = new(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in manifest.Entries)
        {
            var entryPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(inputPath) ?? string.Empty, entry.Path));
            var model = ParseSingle(entryPath, sceneOptions, entry.Transform, materialResolver);
            meshes.AddRange(model.Meshes);

            foreach (var material in model.Materials)
            {
                materials.TryAdd(material.Name, material);
            }
        }

        if (meshes.Count == 0)
        {
            throw new GMConverterException("Unreal scene manifest did not contain any readable mesh parts.");
        }

        return new Model(
            string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(inputPath) : manifest.Name,
            meshes,
            materials.Count == 0 ? [new Material("default")] : materials.Values.ToArray());
    }

    private static Model ParseSingle(string inputPath, ModelParseOptions options, PSKSceneTransform? sceneTransform)
    {
        return ParseSingle(inputPath, options, sceneTransform, PSKMaterialResolver.Create(options.Materials));
    }

    private static Model ParseSingle(
        string inputPath,
        ModelParseOptions options,
        PSKSceneTransform? sceneTransform,
        PSKMaterialResolver materialResolver)
    {
        var psk = PSKFile.Read(inputPath);
        var modelName = Path.GetFileNameWithoutExtension(inputPath);
        var weightLookup = BuildWeightLookup(psk);

        // Pre-resolve materials before the face loop so we can determine each PSK material's
        // effective name. MultiLayerBaker emits a per-part alias (e.g. "MI_X__pab12cd34") into the
        // sidecar so sibling parts that share an upstream material name don't collapse onto one
        // entry in the glTF/MDL material table — we have to use the aliased name as the submesh
        // bucket key here too, otherwise GLTFExporter's name-keyed lookup won't find the right
        // baked textures for this part.
        var distinctMaterials = psk.Materials
            .DistinctBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resolvedMaterials = distinctMaterials
            .Select(material => materialResolver.Resolve(material, modelName, inputPath))
            .ToArray();
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < distinctMaterials.Length; i++)
        {
            aliasMap[distinctMaterials[i].Name] = resolvedMaterials[i].Name;
        }

        List<Vertex> vertices = [];
        Dictionary<string, List<Triangle>> trianglesByMaterial = new(StringComparer.OrdinalIgnoreCase);
        var skippedFaces = 0;

        foreach (var face in psk.Faces)
        {
            if (!TryGetCorner(psk, face.WedgeIndices[2], weightLookup, options, sceneTransform, out var a) ||
                !TryGetCorner(psk, face.WedgeIndices[1], weightLookup, options, sceneTransform, out var b) ||
                !TryGetCorner(psk, face.WedgeIndices[0], weightLookup, options, sceneTransform, out var c))
            {
                skippedFaces++;
                continue;
            }

            var faceNormal = CalculateNormal(a.Position, b.Position, c.Position);
            if (IsDegenerate(faceNormal, a.Position, b.Position, c.Position))
            {
                skippedFaces++;
                continue;
            }

            var originalName = psk.MaterialName(face.MaterialIndex);
            var bucketName = aliasMap.TryGetValue(originalName, out var aliased) ? aliased : originalName;
            if (!trianglesByMaterial.TryGetValue(bucketName, out var triangles))
            {
                triangles = [];
                trianglesByMaterial.Add(bucketName, triangles);
            }

            var vertexOffset = vertices.Count;
            vertices.Add(a.WithFallbackNormal(faceNormal));
            vertices.Add(b.WithFallbackNormal(faceNormal));
            vertices.Add(c.WithFallbackNormal(faceNormal));
            triangles.Add(new Triangle(vertexOffset, vertexOffset + 1, vertexOffset + 2));
        }

        if (vertices.Count == 0)
        {
            throw new GMConverterException(skippedFaces == 0
                ? "PSK contained no mesh triangles."
                : $"PSK contained no valid mesh triangles. Skipped {skippedFaces} invalid face(s).");
        }

        var mesh = new Mesh(
            vertices,
            trianglesByMaterial.Select(pair => new Submesh(pair.Key, pair.Value)).ToArray(),
            Name: modelName);
        var materials = resolvedMaterials.Length > 0
            ? resolvedMaterials
            : [new Material("default")];

        var skeleton = BuildSkeleton(psk, options);

        return new Model(modelName, [mesh], materials, skeleton, BuildAnimations(skeleton, options));
    }

    private static bool TryGetCorner(
        PSKFile psk,
        int wedgeIndex,
        IReadOnlyDictionary<int, IReadOnlyList<VertexBoneWeight>> weightLookup,
        ModelParseOptions options,
        PSKSceneTransform? sceneTransform,
        out PSKCorner corner)
    {
        corner = default;

        if (wedgeIndex < 0 || wedgeIndex >= psk.Wedges.Count)
        {
            return false;
        }

        var wedge = psk.Wedges[wedgeIndex];
        if (wedge.PointIndex < 0 || wedge.PointIndex >= psk.Points.Count)
        {
            return false;
        }

        var rawPosition = sceneTransform?.TransformPosition(psk.Points[wedge.PointIndex]) ?? psk.Points[wedge.PointIndex];
        var position = TransformPosition(rawPosition, options);
        var normal = wedge.PointIndex < psk.VertexNormals.Count
            ? TransformNormal(sceneTransform?.TransformNormal(psk.VertexNormals[wedge.PointIndex]) ?? psk.VertexNormals[wedge.PointIndex], options)
            : Vector3.Zero;
        normal = NormalizeOrZero(normal);

        corner = new PSKCorner(
            position,
            normal,
            new Vector2(wedge.U, 1.0f - wedge.V),
            weightLookup.GetValueOrDefault(wedge.PointIndex));
        return true;
    }

    private static Dictionary<int, IReadOnlyList<VertexBoneWeight>> BuildWeightLookup(PSKFile psk)
    {
        Dictionary<int, List<VertexBoneWeight>> weightsByPoint = [];

        foreach (var weight in psk.Weights)
        {
            if (weight.Weight <= 0 ||
                weight.PointIndex < 0 ||
                weight.PointIndex >= psk.Points.Count ||
                weight.BoneIndex < 0 ||
                weight.BoneIndex >= psk.Bones.Count)
            {
                continue;
            }

            if (!weightsByPoint.TryGetValue(weight.PointIndex, out var pointWeights))
            {
                pointWeights = [];
                weightsByPoint.Add(weight.PointIndex, pointWeights);
            }

            pointWeights.Add(new VertexBoneWeight(weight.BoneIndex, weight.Weight));
        }

        Dictionary<int, IReadOnlyList<VertexBoneWeight>> normalized = [];

        foreach (var (pointIndex, pointWeights) in weightsByPoint)
        {
            var sum = pointWeights.Sum(weight => weight.Weight);
            if (sum <= 0)
            {
                continue;
            }

            normalized[pointIndex] = pointWeights
                .GroupBy(weight => weight.BoneIndex)
                .Select(group => new VertexBoneWeight(group.Key, group.Sum(weight => weight.Weight) / sum))
                .Where(weight => weight.Weight > 0)
                .OrderByDescending(weight => weight.Weight)
                .ToArray();
        }

        return normalized;
    }

    private static Skeleton? BuildSkeleton(PSKFile psk, ModelParseOptions options)
    {
        if (psk.Bones.Count == 0)
        {
            return null;
        }

        var bones = psk.Bones
            .Select((bone, index) =>
            {
                var parentIndex = NormalizeParentIndex(index, bone.ParentIndex, psk.Bones.Count);
                return new Bone(
                    index,
                    bone.Name,
                    parentIndex,
                    new Transform(
                        TransformPosition(bone.Location, options),
                        TransformBoneRotation(bone.Rotation, options, parentIndex >= 0),
                        Vector3.One));
            })
            .ToArray();

        return new Skeleton(bones);
    }

    private static List<AnimationClip>? BuildAnimations(Skeleton? skeleton, ModelParseOptions options)
    {
        if (skeleton is null || string.IsNullOrWhiteSpace(options.AnimationPath))
        {
            return null;
        }

        var animationPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.AnimationPath));
        if (!File.Exists(animationPath))
        {
            throw new GMConverterException($"Animation file not found: {animationPath}");
        }

        var psa = PSAFile.Read(animationPath);
        if (psa.Bones.Count == 0 || psa.Sequences.Count == 0)
        {
            return [];
        }

        var boneNameToIndex = skeleton.Bones
            .GroupBy(bone => NormalizeBoneName(bone.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var psaBoneToSkeletonBone = psa.Bones
            .Select(bone => boneNameToIndex.TryGetValue(NormalizeBoneName(bone.Name), out var boneIndex) ? boneIndex : -1)
            .ToArray();
        List<AnimationClip> clips = [];

        foreach (var sequence in psa.Sequences)
        {
            var fps = sequence.Fps > 0.000001f ? sequence.Fps : 30.0f;
            var keys = psa.GetSequenceKeys(sequence);
            var scaleKeys = psa.GetSequenceScaleKeys(sequence);
            List<IAnimationTrack> tracks = [];

            for (var psaBoneIndex = 0; psaBoneIndex < psa.Bones.Count; psaBoneIndex++)
            {
                var skeletonBoneIndex = psaBoneToSkeletonBone[psaBoneIndex];
                if (skeletonBoneIndex < 0)
                {
                    continue;
                }

                List<TransformKeyframe> keyframes = [];
                for (var frameIndex = 0; frameIndex < sequence.FrameCount; frameIndex++)
                {
                    var keyIndex = checked(frameIndex * psa.Bones.Count + psaBoneIndex);
                    var key = keys[keyIndex];
                    var scale = scaleKeys.Count == keys.Count
                        ? TransformScale(scaleKeys[keyIndex].Scale, options)
                        : Vector3.One;
                    var skeletonBone = skeleton.Bones[skeletonBoneIndex];
                    var transform = new Transform(
                        TransformPosition(key.Location, options),
                        TransformBoneRotation(key.Rotation, options, skeletonBone.ParentIndex >= 0),
                        scale);
                    keyframes.Add(new TransformKeyframe(frameIndex / fps, transform));
                }

                if (keyframes.Count > 0)
                {
                    tracks.Add(new BoneTransformTrack(skeletonBoneIndex, keyframes));
                }
            }

            if (tracks.Count == 0)
            {
                continue;
            }

            clips.Add(new AnimationClip(
                sequence.Name,
                fps,
                sequence.FrameCount <= 1 ? 0.0f : (sequence.FrameCount - 1) / fps,
                tracks));
        }

        return clips;
    }

    private static string NormalizeBoneName(string name)
    {
        return name.TrimEnd();
    }

    private static int NormalizeParentIndex(int boneIndex, int parentIndex, int boneCount)
    {
        if (boneIndex == 0 && parentIndex == 0)
        {
            return -1;
        }

        return parentIndex >= 0 && parentIndex < boneCount && parentIndex != boneIndex
            ? parentIndex
            : -1;
    }

    private static Vector3 TransformPosition(Vector3 position, ModelParseOptions options)
    {
        var scaled = position * options.ScaleFactor;
        return ModelAxisTransforms.TransformPosition(scaled, options.AxisMode, "psk");
    }

    private static Vector3 TransformNormal(Vector3 normal, ModelParseOptions options)
    {
        return ModelAxisTransforms.TransformNormal(normal, options.AxisMode, "psk");
    }

    private static Vector3 TransformScale(Vector3 scale, ModelParseOptions options)
    {
        return ModelAxisTransforms.TransformScale(scale, options.AxisMode, "psk");
    }

    private static Quaternion TransformRotation(Quaternion rotation, ModelParseOptions options)
    {
        var normalized = NormalizeRotation(rotation);
        if (options.AxisMode is not ModelAxisMode.YUp)
        {
            return normalized;
        }

        var basis = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);
        var matrix = Matrix4x4.CreateFromQuaternion(normalized);
        var transformed = basis * matrix * basis;

        return NormalizeRotation(Quaternion.CreateFromRotationMatrix(transformed));
    }

    private static Quaternion TransformBoneRotation(Quaternion rotation, ModelParseOptions options, bool hasParent)
    {
        return TransformRotation(hasParent ? Quaternion.Conjugate(rotation) : rotation, options);
    }

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        var lengthSquared = rotation.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            return Quaternion.Identity;
        }

        return Quaternion.Normalize(rotation);
    }

    private static Vector3 NormalizeOrZero(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            return Vector3.Zero;
        }

        var length = value.Length();
        return length <= 0.000001f ? Vector3.Zero : value / length;
    }

    private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        return NormalizeOrZero(Vector3.Cross(b - a, c - a));
    }

    private static bool IsDegenerate(Vector3 normal, Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).LengthSquared() <= 0.000000000001f || normal.LengthSquared() <= 0.000001f;
    }

    private readonly record struct PSKCorner(
        Vector3 Position,
        Vector3 Normal,
        Vector2 TextureCoordinate,
        IReadOnlyList<VertexBoneWeight>? BoneWeights)
    {
        public Vertex WithFallbackNormal(Vector3 fallbackNormal)
        {
            return new Vertex(Position, Normal == Vector3.Zero ? fallbackNormal : Normal, TextureCoordinate, BoneWeights);
        }
    }

    private static bool IsSceneManifest(string inputPath)
    {
        return string.Equals(Path.GetExtension(inputPath), ".ue4scene", StringComparison.OrdinalIgnoreCase);
    }

    private static PSKSceneManifest ReadSceneManifest(string inputPath)
    {
        return JsonSerializer.Deserialize<PSKSceneManifest>(File.ReadAllText(inputPath))
            ?? throw new GMConverterException($"Invalid Unreal scene manifest: {inputPath}");
    }

    private sealed record PSKSceneManifest(int Version, string Name, IReadOnlyList<PSKSceneEntry> Entries);

    private sealed record PSKSceneEntry(string Path, PSKSceneTransform Transform);

    private sealed record PSKSceneTransform(
        PSKSceneVector3 Translation,
        PSKSceneQuaternion Rotation,
        PSKSceneVector3 Scale)
    {
        public Vector3 TransformPosition(Vector3 position)
        {
            return Vector3.Transform(position * Scale.ToVector3(), Rotation.ToQuaternion()) + Translation.ToVector3();
        }

        public Vector3 TransformNormal(Vector3 normal)
        {
            return Vector3.Transform(normal, Rotation.ToQuaternion());
        }
    }

    private sealed record PSKSceneVector3(float X, float Y, float Z)
    {
        public Vector3 ToVector3()
        {
            return new Vector3(X, Y, Z);
        }
    }

    private sealed record PSKSceneQuaternion(float X, float Y, float Z, float W)
    {
        public Quaternion ToQuaternion()
        {
            var quaternion = new Quaternion(X, Y, Z, W);
            return quaternion.LengthSquared() <= 0.000001f ? Quaternion.Identity : Quaternion.Normalize(quaternion);
        }
    }

    private sealed class PSKMaterialResolver
    {
        private static readonly string[] _imageExtensions = [".png", ".tga", ".dds", ".bmp", ".jpg", ".jpeg"];
        private static readonly string[] _baseColorPriorityTerms =
        [
            "BaseColor",
            "Base Color",
            "Diffuse",
            "Albedo",
            "GroundTone",
            "ToneGround",
            "Fuel Color",
            "Color"
        ];
        private readonly Dictionary<string, string> _materialSidecars;
        private readonly Dictionary<string, string> _cueMaterialSidecars;
        private readonly Dictionary<string, string> _images;

        private PSKMaterialResolver(
            Dictionary<string, string> materialSidecars,
            Dictionary<string, string> cueMaterialSidecars,
            Dictionary<string, string> images)
        {
            _materialSidecars = materialSidecars;
            _cueMaterialSidecars = cueMaterialSidecars;
            _images = images;
        }

        public static PSKMaterialResolver Create(MaterialResolveOptions? options)
        {
            if (options is null || string.IsNullOrWhiteSpace(options.SearchDirectory) ||
                !Directory.Exists(options.SearchDirectory))
            {
                return new PSKMaterialResolver([], [], []);
            }

            return new PSKMaterialResolver(
                IndexFiles(options.SearchDirectory, [".mat"]),
                IndexFiles(options.SearchDirectory, [".json"]),
                IndexFiles(options.SearchDirectory, _imageExtensions));
        }

        public Material Resolve(PSKMaterial material, string meshName, string meshPath)
        {
            Dictionary<string, string>? references = null;
            IReadOnlyDictionary<string, CueMaterialColor> colors = new Dictionary<string, CueMaterialColor>(StringComparer.OrdinalIgnoreCase);
            var usesCueMaterial = false;
            System.Numerics.Vector2? bakedUv0Scale = null;
            string? materialAlias = null;
            if (TryGetLocalSidecar(material, meshPath, ".json", out var localCueMaterialPath))
            {
                var cueMaterial = ReadCueMaterial(localCueMaterialPath);
                references = cueMaterial.Textures;
                colors = cueMaterial.Colors;
                bakedUv0Scale = cueMaterial.BakedUv0Scale;
                materialAlias = cueMaterial.MaterialAlias;
                usesCueMaterial = true;
            }
            else if (TryGetLocalSidecar(material, meshPath, ".mat", out var localMaterialPath))
            {
                references = ReadMaterialReferences(localMaterialPath);
            }
            else if (TryGetSidecar(_materialSidecars, material, out var materialPath))
            {
                references = ReadMaterialReferences(materialPath);
            }
            else if (TryGetSidecar(_cueMaterialSidecars, material, out var cueMaterialPath))
            {
                var cueMaterial = ReadCueMaterial(cueMaterialPath);
                references = cueMaterial.Textures;
                colors = cueMaterial.Colors;
                bakedUv0Scale = cueMaterial.BakedUv0Scale;
                materialAlias = cueMaterial.MaterialAlias;
                usesCueMaterial = true;
            }

            var effectiveName = string.IsNullOrWhiteSpace(materialAlias) ? material.Name : materialAlias;

            if (references is null)
            {
                return new Material(effectiveName);
            }

            Texture? diffuseTexture;
            Texture? normalTexture;
            Texture? specularTexture;
            Texture? emissiveTexture;
            if (usesCueMaterial)
            {
                var materialContext = CreateMaterialContext(material.Name, meshName);
                diffuseTexture = TryLoadCueMaterialTexture(materialContext, references, CueTextureKind.Diffuse, references.ContainsKey("Opacity")) ??
                    TryCreateColorTexture(material.Name, colors);
                normalTexture = TryLoadCueMaterialTexture(materialContext, references, CueTextureKind.Normal, hasAlpha: false);
                specularTexture = TryLoadCueMaterialTexture(materialContext, references, CueTextureKind.Specular, hasAlpha: false);
                emissiveTexture = ShouldResolveCueEmissive(materialContext)
                    ? TryLoadCueMaterialTexture(materialContext, references, CueTextureKind.Emissive, hasAlpha: false)
                    : null;
            }
            else
            {
                var layerSuffix = GetFortniteLayerSuffix(material.Name);
                diffuseTexture = TryLoadLayerTexture(references, ["Diffuse"], layerSuffix, references.ContainsKey("Opacity")) ??
                    TryLoadTexture(references, ["Diffuse"], references.ContainsKey("Opacity")) ??
                    TryLoadTextureByName(references, ["diff", "albedo", "basecolor", "base color", "color"], hasAlpha: references.ContainsKey("Opacity")) ??
                    TryCreateColorTexture(material.Name, colors);
                normalTexture =
                    TryLoadLayerTexture(references, ["Normal", "NormalMap", "Normals"], layerSuffix, hasAlpha: false) ??
                    TryLoadTexture(references, ["Normal", "NormalMap", "Normals"], hasAlpha: false) ??
                    TryLoadTextureByName(references, ["normal", "norm", "nrm"], hasAlpha: false) ??
                    TryLoadRelatedTexture(references, ["Diffuse"], ["_normal", "_norm", "_bump"], hasAlpha: false);
                specularTexture = TryLoadLayerTexture(references, ["Specular", "SpecularityMask", "SpecularMasks"], layerSuffix, hasAlpha: false) ??
                    TryLoadTexture(references, ["Specular", "SpecularityMask", "SpecularMasks"], hasAlpha: false) ??
                    TryLoadTextureByName(references, ["spec", "rough", "metal", "orm", "mrao", "packed"], hasAlpha: false);
                emissiveTexture = TryLoadLayerTexture(references, ["Emissive", "SelfIllumination", "SelfIlluminationMask", "SFX_RGB"], layerSuffix, hasAlpha: false) ??
                    TryLoadTexture(references, ["Emissive", "SelfIllumination", "SelfIlluminationMask", "SFX_RGB"], hasAlpha: false) ??
                    TryLoadTextureByName(references, ["emiss", "sfx", "glow"], hasAlpha: false);
            }

            // Diagnostic: dump every material resolution so we can verify, when something looks wrong
            // in the glb, whether PSKImporter actually picked what its sidecar said. Writes alongside
            // the mesh PSK file so the log lives in the same temp tree as the source data.
            try
            {
                var logDir = System.IO.Path.GetDirectoryName(meshPath);
                if (!string.IsNullOrWhiteSpace(logDir))
                {
                    var logPath = System.IO.Path.Combine(logDir, $"{NameHelpers.SanitizeMaterialName(material.Name)}.resolve.log");
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("materialName=").AppendLine(material.Name);
                    sb.Append("meshName=").AppendLine(meshName);
                    sb.Append("meshPath=").AppendLine(meshPath);
                    sb.Append("usesCueMaterial=").AppendLine(usesCueMaterial.ToString(inv));
                    sb.Append("diffuse=").AppendLine(diffuseTexture?.Name ?? "<null>");
                    sb.Append("normal=").AppendLine(normalTexture?.Name ?? "<null>");
                    sb.Append("specular=").AppendLine(specularTexture?.Name ?? "<null>");
                    sb.Append("emissive=").AppendLine(emissiveTexture?.Name ?? "<null>");
                    sb.Append("references.Count=").AppendLine(references.Count.ToString(inv));
                    foreach (var (k, v) in references)
                    {
                        sb.Append("  '").Append(k).Append("' = '").Append(v).AppendLine("'");
                    }
                    System.IO.File.WriteAllText(logPath, sb.ToString());
                }
            }
            catch
            {
                // diagnostics must not fail the import
            }

            return new Material(
                effectiveName,
                diffuseTexture: diffuseTexture,
                specularTexture: specularTexture,
                normalTexture: normalTexture,
                emissiveTexture: emissiveTexture,
                specularTexturePacking: usesCueMaterial
                    ? MaterialSpecularTexturePacking.UnrealSpecularMasks
                    : MaterialSpecularTexturePacking.Standard,
                normalTextureConvention: usesCueMaterial
                    ? MaterialNormalTextureConvention.DirectX
                    : MaterialNormalTextureConvention.OpenGl,
                bakedUv0Scale: bakedUv0Scale,
                specularFactor: usesCueMaterial ? _fortniteSpecularFactor : 1.0f);
        }

        // Fortnite's SpecularMasks.R doesn't drive specular intensity in the in-game renderer (FP's
        // shader.frag reads it into a local but never uses it). The Blender FPv4 graph wires R
        // straight into Principled BSDF's Specular IOR Level which produces visibly too-shiny
        // output; the user empirically calibrated ≈0.01 to match Fortnite's in-game look. Damping
        // here at the import boundary keeps the exporters format-agnostic.
        private const float _fortniteSpecularFactor = 0.01f;

        private static bool TryGetLocalSidecar(
            PSKMaterial material,
            string meshPath,
            string extension,
            out string path)
        {
            var directory = Path.GetDirectoryName(meshPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                foreach (var key in GetMaterialLookupKeys(material))
                {
                    var candidate = Path.Combine(directory, key + extension);
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                        return true;
                    }
                }
            }

            path = string.Empty;
            return false;
        }

        private static bool TryGetSidecar(
            Dictionary<string, string> sidecars,
            PSKMaterial material,
            out string path)
        {
            foreach (var key in GetMaterialLookupKeys(material))
            {
                if (sidecars.TryGetValue(key, out var sidecarPath))
                {
                    path = sidecarPath;
                    return true;
                }
            }

            path = string.Empty;
            return false;
        }

        private static HashSet<string> GetMaterialLookupKeys(PSKMaterial material)
        {
            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
            AddMaterialLookupKey(keys, material.Name);
            AddMaterialLookupKey(keys, material.OriginalName);
            AddMaterialLookupKey(keys, NormalizeReference(material.OriginalName));
            AddMaterialLookupKey(keys, Path.GetFileNameWithoutExtension(material.OriginalName));
            return keys;
        }

        private static void AddMaterialLookupKey(HashSet<string> keys, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var key = NameHelpers.SanitizeMaterialName(value);
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(key);
            }
        }

        private Texture? TryLoadTexture(
            Dictionary<string, string> references,
            IReadOnlyCollection<string> channels,
            bool hasAlpha)
        {
            foreach (var channel in channels)
            {
                if (!references.TryGetValue(channel, out var textureReference) ||
                    IsNullReference(textureReference))
                {
                    continue;
                }

                var texture = TryLoadTexture(textureReference, hasAlpha);
                if (texture is not null)
                {
                    return texture;
                }
            }

            return null;
        }

        private Texture? TryLoadLayerTexture(
            Dictionary<string, string> references,
            IReadOnlyCollection<string> channels,
            string? layerSuffix,
            bool hasAlpha)
        {
            if (string.IsNullOrWhiteSpace(layerSuffix))
            {
                return null;
            }

            foreach (var (key, textureReference) in references)
            {
                if (IsNullReference(textureReference) ||
                    !channels.Any(channel => IsLayerTextureKey(key, channel, layerSuffix)))
                {
                    continue;
                }

                var texture = TryLoadTexture(textureReference, hasAlpha);
                if (texture is not null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static bool IsLayerTextureKey(string key, string channel, string layerSuffix)
        {
            var normalizedKey = key.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            var normalizedChannel = channel.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            return normalizedKey.StartsWith(normalizedChannel, StringComparison.OrdinalIgnoreCase) &&
                normalizedKey.EndsWith(layerSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetFortniteLayerSuffix(string materialName)
        {
            var normalized = NameHelpers.SanitizeMaterialName(materialName);
            if (normalized.EndsWith("_B", StringComparison.OrdinalIgnoreCase))
            {
                return "2";
            }

            if (normalized.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
            {
                return "3";
            }

            if (normalized.EndsWith("_D", StringComparison.OrdinalIgnoreCase))
            {
                return "4";
            }

            if (normalized.EndsWith("_E", StringComparison.OrdinalIgnoreCase))
            {
                return "5";
            }

            if (normalized.EndsWith("_F", StringComparison.OrdinalIgnoreCase))
            {
                return "6";
            }

            if (normalized.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
            {
                return "7";
            }

            return normalized.EndsWith("_H", StringComparison.OrdinalIgnoreCase) ? "8" : null;
        }

        private Texture? TryLoadTextureByName(
            Dictionary<string, string> references,
            IReadOnlyCollection<string> keyTerms,
            bool hasAlpha)
        {
            foreach (var (key, textureReference) in references)
            {
                if (IsNullReference(textureReference) ||
                    !keyTerms.Any(term => key.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var texture = TryLoadTexture(textureReference, hasAlpha);
                if (texture is not null)
                {
                    return texture;
                }
            }

            return null;
        }

        private Texture? TryLoadCueMaterialTexture(
            MaterialTextureContext materialContext,
            Dictionary<string, string> references,
            CueTextureKind textureKind,
            bool hasAlpha)
        {
            var directTexture = TryLoadDirectCueMaterialTexture(materialContext, references, textureKind, hasAlpha);
            if (directTexture is not null)
            {
                return directTexture;
            }

            var candidates = references
                .Where(pair => !IsNullReference(pair.Value) && IsCueTextureCandidate(textureKind, pair.Key, pair.Value))
                .Select(pair => new
                {
                    pair.Value,
                    Score = ScoreCueTextureCandidate(materialContext, textureKind, pair.Key, pair.Value)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();

            foreach (var candidate in candidates)
            {
                var texture = TryLoadTexture(candidate.Value, hasAlpha);
                if (texture is not null)
                {
                    return texture;
                }
            }

            return null;
        }

        private Texture? TryLoadDirectCueMaterialTexture(
            MaterialTextureContext materialContext,
            Dictionary<string, string> references,
            CueTextureKind textureKind,
            bool hasAlpha)
        {
            // Layer-suffix detection must use the MATERIAL name, not the mesh name. Fortnite's _A/_B/
            // _C suffix convention identifies multi-layer MATERIAL variants ("M_Body_Skin_B" = layer
            // 2 of the skin master), but mesh assets are independently named with _A/_B/_C/_A1 to tag
            // shape variants (e.g. `SM_NobleCrest_LAATRear_B.pskx` is the "Rear B" geometry but it
            // still uses the layer-1 material `MI_NobleCrest_LAATRear_A`). Using mesh name caused the
            // LAAT rear/side meshes to pick Diffuse_Texture_2/Normals_Texture_2/SpecularMasks_2 from
            // the master material's secondary layer slot instead of the intended layer 1.
            var slots = GetCueTextureSlots(textureKind, GetFortniteLayerSuffix(materialContext.MaterialName))
                .Select((slot, index) => new { Slot = slot, Rank = index })
                .ToArray();
            var candidates = references
                .Where(pair => !IsNullReference(pair.Value))
                .Select(pair => new
                {
                    pair.Value,
                    Slot = slots.FirstOrDefault(slot => IsSameTextureSlot(slot.Slot, pair.Key))
                })
                .Where(candidate => candidate.Slot is not null)
                .Select(candidate => new
                {
                    candidate.Value,
                    candidate.Slot!.Rank,
                    MeshScore = ScoreTextureNameAgainstTokens(materialContext.MeshTokens, candidate.Value),
                    IsFallback = IsFallbackTextureReference(candidate.Value)
                })
                .OrderBy(candidate => candidate.IsFallback)
                .ThenByDescending(candidate => candidate.MeshScore)
                .ThenBy(candidate => candidate.Rank)
                .ToArray();

            foreach (var candidate in candidates)
            {
                var texture = TryLoadTexture(candidate.Value, hasAlpha);
                if (texture is not null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static IEnumerable<string> GetCueTextureSlots(CueTextureKind textureKind, string? layerSuffix)
        {
            if (!string.IsNullOrWhiteSpace(layerSuffix))
            {
                foreach (var slot in GetCueLayerTextureSlots(textureKind, layerSuffix))
                {
                    yield return slot;
                }
            }

            foreach (var slot in GetCueBaseTextureSlots(textureKind))
            {
                yield return slot;
            }
        }

        private static IEnumerable<string> GetCueLayerTextureSlots(CueTextureKind textureKind, string layerSuffix)
        {
            return textureKind switch
            {
                CueTextureKind.Diffuse => [$"Diffuse_Texture_{layerSuffix}", $"Diffuse Texture {layerSuffix}", $"Diffuse_{layerSuffix}", $"BaseColor_Texture_{layerSuffix}", $"BaseColor_{layerSuffix}"],
                CueTextureKind.Normal => [$"Normals_Texture_{layerSuffix}", $"Normal_Texture_{layerSuffix}", $"Normals_{layerSuffix}", $"NormalMap_{layerSuffix}"],
                CueTextureKind.Specular => [$"SpecularMasks_{layerSuffix}", $"SpecularMasks {layerSuffix}", $"Specular_{layerSuffix}", $"SpecularityMask_{layerSuffix}"],
                CueTextureKind.Emissive => [$"Emissive_Texture_{layerSuffix}", $"EmissiveTexture_{layerSuffix}", $"Emissive_{layerSuffix}", $"Emission_{layerSuffix}"],
                _ => []
            };
        }

        private static IEnumerable<string> GetCueBaseTextureSlots(CueTextureKind textureKind)
        {
            return textureKind switch
            {
                CueTextureKind.Diffuse => ["Diffuse", "D", "Base Color", "BaseColor", "Diffuse Texture", "Diffuse_Texture", "BaseColorTexture", "PM_Diffuse"],
                CueTextureKind.Normal => ["Normals", "Normal", "NormalMap", "N", "Normal Texture", "NormalTexture", "PM_Normals"],
                CueTextureKind.Specular => ["SpecularMasks", "Specular", "SpecularityMask", "Specular Mask", "SpecularMask", "S", "SRM", "PM_SpecularMasks"],
                CueTextureKind.Emissive => ["Emission", "Emissive", "EmissiveColor", "EmissiveTexture", "PM_Emissive"],
                _ => []
            };
        }

        private static bool IsSameTextureSlot(string expected, string actual)
        {
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    expected.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase),
                    actual.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static int ScoreTextureNameAgainstTokens(IReadOnlyList<string> tokens, string textureReference)
        {
            var textureName = NameHelpers.SanitizeMaterialName(NormalizeReference(textureReference));
            return tokens.Sum(token => textureName.Contains(token, StringComparison.OrdinalIgnoreCase) ? Math.Min(token.Length, 12) : 0);
        }

        private static bool IsFallbackTextureReference(string textureReference)
        {
            var normalizedReference = textureReference.Replace('\\', '/');
            return ContainsAny(normalizedReference, ["/Game/Global/", "/Engine/", "/Landscape/"]);
        }

        private static bool IsCueTextureCandidate(CueTextureKind textureKind, string key, string textureReference)
        {
            var normalizedKey = NameHelpers.SanitizeMaterialName(key);
            var textureName = NameHelpers.SanitizeMaterialName(NormalizeReference(textureReference));
            return textureKind switch
            {
                CueTextureKind.Diffuse => ContainsAny(normalizedKey, ["diff", "albedo", "basecolor"]) ||
                    textureName.EndsWith("_D", StringComparison.OrdinalIgnoreCase),
                CueTextureKind.Normal => ContainsAny(normalizedKey, ["normal", "norm", "nrm"]) ||
                    textureName.EndsWith("_N", StringComparison.OrdinalIgnoreCase),
                CueTextureKind.Specular => ContainsAny(normalizedKey, ["spec", "rough", "metal", "orm", "mrao", "packed"]) ||
                    textureName.EndsWith("_S", StringComparison.OrdinalIgnoreCase),
                CueTextureKind.Emissive => ContainsAny(normalizedKey, ["emiss", "glow", "sfx"]),
                _ => false
            };
        }

        private static int ScoreCueTextureCandidate(
            MaterialTextureContext materialContext,
            CueTextureKind textureKind,
            string key,
            string textureReference)
        {
            var normalizedKey = NameHelpers.SanitizeMaterialName(key);
            var textureName = NameHelpers.SanitizeMaterialName(NormalizeReference(textureReference));
            var normalizedReference = textureReference.Replace('\\', '/');
            var score = GetCueTextureKindScore(textureKind, normalizedKey, textureName);

            foreach (var token in materialContext.Tokens)
            {
                if (textureName.Contains(token.Value, StringComparison.OrdinalIgnoreCase))
                {
                    score += Math.Min(token.Value.Length, 12) * token.Weight;
                }
            }

            if (ContainsAny(normalizedReference, ["/Game/Global/", "/Engine/", "/Landscape/"]))
            {
                score -= 80;
            }

            if (ContainsAny(normalizedKey, ["snow", "cover", "nanite", "wpo", "wind", "vegetable", "noise", "leaks", "velvet", "rvt", "vnr", "dfx", "blockout"]))
            {
                score -= 60;
            }

            return score;
        }

        private static MaterialTextureContext CreateMaterialContext(string materialName, string meshName)
        {
            var meshTokens = GetMaterialTextureTokens(meshName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var tokens = GetMaterialTextureTokens(materialName)
                .Select(token => new MaterialTextureToken(token, 3))
                .Concat(meshTokens.Select(token => new MaterialTextureToken(token, 8)))
                .GroupBy(token => token.Value, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(token => token.Weight).First())
                .ToArray();

            return new MaterialTextureContext(materialName, meshName, meshTokens, tokens);
        }

        private static bool ShouldResolveCueEmissive(MaterialTextureContext context)
        {
            return ContainsAny(context.MaterialName, ["emiss", "glow", "holo", "hologram", "neon"]) ||
                ContainsAny(context.MeshName, ["emiss", "glow", "holo", "hologram", "neon"]);
        }

        private static int GetCueTextureKindScore(CueTextureKind textureKind, string key, string textureName)
        {
            return textureKind switch
            {
                CueTextureKind.Diffuse when textureName.EndsWith("_D", StringComparison.OrdinalIgnoreCase) => 120,
                CueTextureKind.Normal when textureName.EndsWith("_N", StringComparison.OrdinalIgnoreCase) => 120,
                CueTextureKind.Specular when textureName.EndsWith("_S", StringComparison.OrdinalIgnoreCase) => 120,
                CueTextureKind.Emissive when ContainsAny(key, ["emiss", "glow"]) => 80,
                CueTextureKind.Emissive when key.Contains("sfx", StringComparison.OrdinalIgnoreCase) => 20,
                _ => 20
            };
        }

        private static IEnumerable<string> GetMaterialTextureTokens(string materialName)
        {
            var normalized = NameHelpers.SanitizeMaterialName(materialName)
                .Replace("MI_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("M_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("T_", string.Empty, StringComparison.OrdinalIgnoreCase);
            foreach (var token in normalized.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsUsefulMaterialTextureToken(token))
                {
                    yield return token;
                }

                foreach (var segment in SplitCamelCaseToken(token))
                {
                    if (IsUsefulMaterialTextureToken(segment))
                    {
                        yield return segment;
                    }
                }
            }
        }

        private static IEnumerable<string> SplitCamelCaseToken(string token)
        {
            var start = 0;
            for (var i = 1; i < token.Length; i++)
            {
                if (char.IsUpper(token[i]) && (char.IsLower(token[i - 1]) || i + 1 < token.Length && char.IsLower(token[i + 1])))
                {
                    yield return token[start..i];
                    start = i;
                }
            }

            yield return token[start..];
        }

        private static bool IsUsefulMaterialTextureToken(string token)
        {
            return token.Length >= 3 &&
                !token.Equals("mat", StringComparison.OrdinalIgnoreCase) &&
                !token.Equals("material", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string value, IReadOnlyCollection<string> terms)
        {
            return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private Texture? TryLoadRelatedTexture(
            Dictionary<string, string> references,
            IReadOnlyCollection<string> baseChannels,
            IReadOnlyCollection<string> suffixes,
            bool hasAlpha)
        {
            foreach (var baseChannel in baseChannels)
            {
                if (!references.TryGetValue(baseChannel, out var baseReference) ||
                    IsNullReference(baseReference))
                {
                    continue;
                }

                var baseName = NameHelpers.SanitizeMaterialName(baseReference);
                foreach (var suffix in suffixes)
                {
                    var relatedName = $"{baseName}{suffix}";
                    if (_images.ContainsKey(relatedName))
                    {
                        return TryLoadTexture(relatedName, hasAlpha);
                    }
                }
            }

            return null;
        }

        private Texture? TryLoadTexture(string textureReference, bool hasAlpha)
        {
            var textureName = NameHelpers.SanitizeMaterialName(textureReference);
            if (!_images.TryGetValue(textureName, out var imagePath))
            {
                return null;
            }

            try
            {
                var image = Image.Load<Rgba32>(imagePath);
                if (!hasAlpha)
                {
                    image.ProcessPixelRows(accessor =>
                    {
                        for (var y = 0; y < accessor.Height; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            for (var x = 0; x < row.Length; x++)
                            {
                                var pixel = row[x];
                                pixel.A = byte.MaxValue;
                                row[x] = pixel;
                            }
                        }
                    });
                }

                return new Texture(textureName, image, hasAlpha);
            }
            catch
            {
                return null;
            }
        }

        private static Texture? TryCreateColorTexture(
            string materialName,
            IReadOnlyDictionary<string, CueMaterialColor> colors)
        {
            if (!TrySelectBaseColor(colors, out var color))
            {
                return null;
            }

            var textureName = $"{NameHelpers.SanitizeMaterialName(materialName)}_color";
            var pixel = new Rgba32(
                ToColorByte(color.R),
                ToColorByte(color.G),
                ToColorByte(color.B),
                byte.MaxValue);
            var image = new Image<Rgba32>(1, 1, pixel);
            return new Texture(textureName, image);
        }

        private static bool TrySelectBaseColor(
            IReadOnlyDictionary<string, CueMaterialColor> colors,
            out CueMaterialColor color)
        {
            foreach (var term in _baseColorPriorityTerms)
            {
                foreach (var (key, candidate) in colors)
                {
                    if (key.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                        IsUsableBaseColor(key, candidate))
                    {
                        color = candidate;
                        return true;
                    }
                }
            }

            foreach (var (key, candidate) in colors)
            {
                if (IsUsableBaseColor(key, candidate))
                {
                    color = candidate;
                    return true;
                }
            }

            color = default;
            return false;
        }

        private static bool IsUsableBaseColor(string key, CueMaterialColor color)
        {
            if (key.Contains("normal", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("direction", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("location", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("loc", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("offset", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("scale", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("speed", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("tile", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("uv", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("mask", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("debug", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return float.IsFinite(color.R) &&
                float.IsFinite(color.G) &&
                float.IsFinite(color.B) &&
                ColorLuminance(color) > 0.02f;
        }

        private static byte ToColorByte(float value)
        {
            if (!float.IsFinite(value))
            {
                return 0;
            }

            return (byte)Math.Clamp(MathF.Round(Math.Clamp(value, 0.0f, 1.0f) * byte.MaxValue), 0.0f, byte.MaxValue);
        }

        private static float ColorLuminance(CueMaterialColor color)
        {
            return Math.Clamp(color.R, 0.0f, 1.0f) * 0.2126f +
                Math.Clamp(color.G, 0.0f, 1.0f) * 0.7152f +
                Math.Clamp(color.B, 0.0f, 1.0f) * 0.0722f;
        }

        private static bool IsNullReference(string textureReference)
        {
            return string.IsNullOrWhiteSpace(textureReference) ||
                string.Equals(textureReference.Trim(), "none", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ReadMaterialReferences(string materialPath)
        {
            Dictionary<string, string> references = new(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(materialPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator <= 0 || separator == trimmed.Length - 1)
                {
                    continue;
                }

                var key = trimmed[..separator].Trim();
                var value = NormalizeReference(trimmed[(separator + 1)..]);
                if (value.Length == 0)
                {
                    continue;
                }

                references.TryAdd(key, value);
            }

            return references;
        }

        private static CueMaterial ReadCueMaterial(string materialPath)
        {
            Dictionary<string, string> references = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, CueMaterialColor> colors = new(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(File.ReadAllText(materialPath));
            if (document.RootElement.TryGetProperty("Textures", out var textures) &&
                textures.ValueKind is JsonValueKind.Object)
            {
                foreach (var property in textures.EnumerateObject())
                {
                    var textureReference = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(textureReference))
                    {
                        continue;
                    }

                    references.TryAdd(property.Name, NormalizeReference(textureReference));
                }
            }

            if (document.RootElement.TryGetProperty("Parameters", out var parameters) &&
                parameters.ValueKind is JsonValueKind.Object &&
                parameters.TryGetProperty("Colors", out var colorParameters) &&
                colorParameters.ValueKind is JsonValueKind.Object)
            {
                foreach (var property in colorParameters.EnumerateObject())
                {
                    if (TryReadCueMaterialColor(property.Value, out var color))
                    {
                        colors.TryAdd(property.Name, color);
                    }
                }
            }

            System.Numerics.Vector2? bakedUv0Scale = null;
            if (document.RootElement.TryGetProperty("BakedUv0Scale", out var scaleEl) &&
                scaleEl.ValueKind is JsonValueKind.Object &&
                TryGetFloat(scaleEl, "X", out var scaleX) &&
                TryGetFloat(scaleEl, "Y", out var scaleY))
            {
                bakedUv0Scale = new System.Numerics.Vector2(scaleX, scaleY);
            }

            string? materialAlias = null;
            if (document.RootElement.TryGetProperty("MaterialAlias", out var aliasEl) &&
                aliasEl.ValueKind is JsonValueKind.String)
            {
                materialAlias = aliasEl.GetString();
            }

            return new CueMaterial(references, colors, bakedUv0Scale, materialAlias);
        }

        private static bool TryReadCueMaterialColor(JsonElement element, out CueMaterialColor color)
        {
            if (element.ValueKind is not JsonValueKind.Object ||
                !TryGetFloat(element, "R", out var r) ||
                !TryGetFloat(element, "G", out var g) ||
                !TryGetFloat(element, "B", out var b))
            {
                color = default;
                return false;
            }

            var a = TryGetFloat(element, "A", out var alpha) ? alpha : 1.0f;
            color = new CueMaterialColor(r, g, b, a);
            return true;
        }

        private static bool TryGetFloat(JsonElement element, string propertyName, out float value)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind is JsonValueKind.Number)
            {
                value = (float)property.GetDouble();
                return true;
            }

            value = 0;
            return false;
        }

        private static string NormalizeReference(string value)
        {
            var normalized = value.Trim().Trim('"', '\'');

            var quotedReferenceStart = normalized.IndexOf('\'');
            var quotedReferenceEnd = normalized.LastIndexOf('\'');
            if (quotedReferenceStart >= 0 && quotedReferenceEnd > quotedReferenceStart)
            {
                normalized = normalized[(quotedReferenceStart + 1)..quotedReferenceEnd];
            }

            normalized = normalized.Replace('\\', '/');
            var slashIndex = normalized.LastIndexOf('/');
            if (slashIndex >= 0)
            {
                normalized = normalized[(slashIndex + 1)..];
            }

            var dotIndex = normalized.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                normalized = normalized[(dotIndex + 1)..];
            }

            return normalized.Trim();
        }

        private static Dictionary<string, string> IndexFiles(string directory, IReadOnlyCollection<string> extensions)
        {
            Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(ImageExtensionPriority)
                         .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(file);
                if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = NameHelpers.SanitizeMaterialName(Path.GetFileNameWithoutExtension(file));
                index.TryAdd(key, file);
            }

            return index;
        }

        private static int ImageExtensionPriority(string path)
        {
            var extension = Path.GetExtension(path);
            var index = Array.FindIndex(_imageExtensions, item => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? _imageExtensions.Length : index;
        }

        private sealed record CueMaterial(
            Dictionary<string, string> Textures,
            Dictionary<string, CueMaterialColor> Colors,
            System.Numerics.Vector2? BakedUv0Scale = null,
            string? MaterialAlias = null);

        private sealed record MaterialTextureContext(
            string MaterialName,
            string MeshName,
            IReadOnlyList<string> MeshTokens,
            IReadOnlyList<MaterialTextureToken> Tokens);

        private readonly record struct MaterialTextureToken(string Value, int Weight);

        private readonly record struct CueMaterialColor(float R, float G, float B, float A);

        private enum CueTextureKind
        {
            Diffuse,
            Normal,
            Specular,
            Emissive
        }
    }
}
