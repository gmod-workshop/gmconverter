using System.Numerics;
using GMConverter.Common;
using GMConverter.Formats.PSA;
using GMConverter.Formats.PSK;
using GMConverter.Geometry;
using ImageMagick;

namespace GMConverter.Importers;

internal sealed class PSKImporter : IImporter
{
    public string InputFormat => "psk";

    public string InputName => "Unreal Engine";

    public object Summarize(string inputPath)
    {
        return PSKSummary.From(inputPath, PSKFile.Read(inputPath));
    }

    public Model Parse(string inputPath, ModelParseOptions options)
    {
        var psk = PSKFile.Read(inputPath);
        var modelName = Path.GetFileNameWithoutExtension(inputPath);
        var weightLookup = BuildWeightLookup(psk);
        List<Vertex> vertices = [];
        Dictionary<string, List<Triangle>> trianglesByMaterial = new(StringComparer.OrdinalIgnoreCase);
        var skippedFaces = 0;

        foreach (var face in psk.Faces)
        {
            if (!TryGetCorner(psk, face.WedgeIndices[2], weightLookup, options, out var a) ||
                !TryGetCorner(psk, face.WedgeIndices[1], weightLookup, options, out var b) ||
                !TryGetCorner(psk, face.WedgeIndices[0], weightLookup, options, out var c))
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

            var materialName = psk.MaterialName(face.MaterialIndex);
            if (!trianglesByMaterial.TryGetValue(materialName, out var triangles))
            {
                triangles = [];
                trianglesByMaterial.Add(materialName, triangles);
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
            trianglesByMaterial.Select(pair => new Submesh(pair.Key, pair.Value)).ToArray());
        var materialResolver = PSKMaterialResolver.Create(options.Materials);
        var materials = psk.Materials
            .DistinctBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .Select(material => materialResolver.Resolve(material))
            .ToArray();

        if (materials.Length == 0)
        {
            materials = [new Material("default")];
        }

        var skeleton = BuildSkeleton(psk, options);

        return new Model(modelName, [mesh], materials, skeleton, BuildAnimations(skeleton, options));
    }

    private static bool TryGetCorner(
        PSKFile psk,
        int wedgeIndex,
        IReadOnlyDictionary<int, IReadOnlyList<VertexBoneWeight>> weightLookup,
        ModelParseOptions options,
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

        var position = TransformPosition(psk.Points[wedge.PointIndex], options);
        var normal = wedge.PointIndex < psk.VertexNormals.Count
            ? TransformNormal(psk.VertexNormals[wedge.PointIndex], options)
            : Vector3.Zero;

        corner = new PSKCorner(
            position,
            NormalizeOrZero(normal),
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

    private static IReadOnlyList<AnimationClip>? BuildAnimations(Skeleton? skeleton, ModelParseOptions options)
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

    private sealed class PSKMaterialResolver
    {
        private static readonly string[] ImageExtensions = [".png", ".tga", ".dds", ".bmp", ".jpg", ".jpeg"];
        private readonly Dictionary<string, string> materialSidecars;
        private readonly Dictionary<string, string> images;

        private PSKMaterialResolver(Dictionary<string, string> materialSidecars, Dictionary<string, string> images)
        {
            this.materialSidecars = materialSidecars;
            this.images = images;
        }

        public static PSKMaterialResolver Create(MaterialResolveOptions? options)
        {
            if (options is null || string.IsNullOrWhiteSpace(options.SearchDirectory) ||
                !Directory.Exists(options.SearchDirectory))
            {
                return new PSKMaterialResolver([], []);
            }

            return new PSKMaterialResolver(
                IndexFiles(options.SearchDirectory, [".mat"]),
                IndexFiles(options.SearchDirectory, ImageExtensions));
        }

        public Material Resolve(PSKMaterial material)
        {
            if (!materialSidecars.TryGetValue(material.Name, out var materialPath))
            {
                return new Material(material.Name);
            }

            var references = ReadMaterialReferences(materialPath);
            var diffuseTexture = TryLoadTexture(references, ["Diffuse"], references.ContainsKey("Opacity"));
            var normalTexture =
                TryLoadTexture(references, ["Normal", "NormalMap"], hasAlpha: false) ??
                TryLoadRelatedTexture(references, ["Diffuse"], ["_normal", "_norm", "_bump"], hasAlpha: false);
            var specularTexture = TryLoadTexture(references, ["Specular", "SpecularityMask"], hasAlpha: false);
            var emissiveTexture = TryLoadTexture(references, ["Emissive", "SelfIllumination", "SelfIlluminationMask"], hasAlpha: false);

            return new Material(
                material.Name,
                diffuseTexture: diffuseTexture,
                specularTexture: specularTexture,
                normalTexture: normalTexture,
                emissiveTexture: emissiveTexture);
        }

        private Texture? TryLoadTexture(
            IReadOnlyDictionary<string, string> references,
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

        private Texture? TryLoadRelatedTexture(
            IReadOnlyDictionary<string, string> references,
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
                    if (images.ContainsKey(relatedName))
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
            if (!images.TryGetValue(textureName, out var imagePath))
            {
                return null;
            }

            try
            {
                var image = new MagickImage(imagePath);
                if (!hasAlpha && image.HasAlpha)
                {
                    image.Alpha(AlphaOption.Opaque);
                }

                return new Texture(textureName, image, hasAlpha);
            }
            catch
            {
                return null;
            }
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
            var index = Array.FindIndex(ImageExtensions, item => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? ImageExtensions.Length : index;
        }
    }
}
