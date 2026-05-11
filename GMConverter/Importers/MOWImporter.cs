using System.Globalization;
using System.Numerics;
using GMConverter.Common;
using GMConverter.Formats.MOW;
using GMConverter.Geometry;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GMConverter.Importers;

internal sealed class MOWImporter : IImporter
{
    private const float AnimationFrameRate = 30.0f;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MOWImporter> _logger;

    public MOWImporter(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<MOWImporter>();
    }

    public string InputFormat => "mow";

    public string InputName => "Men of War";

    public object Summarize(string inputPath)
    {
        var modelPath = ResolveModelPath(inputPath);
        return MOWSummary.From(inputPath, modelPath, MOWModelFile.Read(modelPath));
    }

    public Model Parse(string inputPath, ModelParseOptions options)
    {
        var modelPath = ResolveModelPath(inputPath);
        var modelFile = MOWModelFile.Read(modelPath);
        var modelDirectory = Path.GetDirectoryName(modelPath) ?? ".";
        List<Mesh> meshes = [];
        Dictionary<string, Material> materials = new(StringComparer.OrdinalIgnoreCase);
        List<Bone> bones = [];
        Dictionary<string, List<int>> boneIndicesByMOWName = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedBoneNames = new(StringComparer.OrdinalIgnoreCase);
        var textureResolver = MOWTextureResolver.Create(modelDirectory, options.Materials, _loggerFactory);

        var skeleton = modelFile.Root.FirstChild("Skeleton")
            ?? throw new GMConverterException($"Men of War MDL does not contain a Skeleton node: {modelPath}");

        foreach (var bone in skeleton.Children.Where(IsBoneNode))
        {
            AddBoneMeshes(
                bone,
                parentIndex: -1,
                Matrix4x4.Identity,
                modelDirectory,
                options,
                bones,
                boneIndicesByMOWName,
                usedBoneNames,
                meshes,
                materials,
                textureResolver);
        }

        if (meshes.Count == 0)
        {
            throw new GMConverterException($"Men of War model contains no VolumeView meshes: {modelPath}");
        }

        var animations = LoadAnimations(modelFile.Root, modelDirectory, bones, boneIndicesByMOWName, options, _logger);

        return new Model(
            Path.GetFileNameWithoutExtension(modelPath),
            meshes,
            materials.Values.ToArray(),
            bones.Count == 0 ? null : new Skeleton(bones),
            animations.Count == 0 ? null : animations);
    }

    private static string ResolveModelPath(string inputPath)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(inputPath));
        var extension = Path.GetExtension(fullPath);

        if (string.Equals(extension, ".def", StringComparison.OrdinalIgnoreCase))
        {
            var modelPath = MOWDefinitionFile.Read(fullPath).ResolveModelPath();
            if (!File.Exists(modelPath))
            {
                throw new GMConverterException($"Men of War DEF references a missing MDL file: {modelPath}");
            }

            return modelPath;
        }

        if (string.Equals(extension, ".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        throw new GMConverterException($"Expected a Men of War .def or .mdl file: {fullPath}");
    }

    private static void AddBoneMeshes(
        MOWNode bone,
        int parentIndex,
        Matrix4x4 parentTransform,
        string modelDirectory,
        ModelParseOptions options,
        List<Bone> bones,
        Dictionary<string, List<int>> boneIndicesByMOWName,
        HashSet<string> usedBoneNames,
        List<Mesh> meshes,
        Dictionary<string, Material> materials,
        MOWTextureResolver textureResolver)
    {
        var localTransform = GetLocalTransform(bone);
        var worldTransform = localTransform * parentTransform;
        var boneIndex = bones.Count;
        var mowBoneName = GetMOWBoneName(bone, boneIndex);
        var boneName = MakeUniqueBoneName(mowBoneName, usedBoneNames);
        bones.Add(new Bone(
            boneIndex,
            boneName,
            parentIndex,
            ConvertBoneTransform(localTransform, options)));
        AddBoneNameLookup(boneIndicesByMOWName, mowBoneName, boneIndex);
        if (!string.Equals(mowBoneName, boneName, StringComparison.OrdinalIgnoreCase))
        {
            AddBoneNameLookup(boneIndicesByMOWName, boneName, boneIndex);
        }

        foreach (var meshFileName in GetMeshFileNames(bone).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var meshPath = Path.GetFullPath(Path.Combine(modelDirectory, meshFileName));
            if (!File.Exists(meshPath))
            {
                throw new GMConverterException($"Men of War mesh file not found for bone '{mowBoneName}': {meshPath}");
            }

            meshes.Add(ConvertMesh(MOWPlyFile.Read(meshPath), worldTransform, boneIndex, options, modelDirectory, materials, textureResolver));
        }

        foreach (var childBone in bone.Children.Where(IsBoneNode))
        {
            AddBoneMeshes(
                childBone,
                boneIndex,
                worldTransform,
                modelDirectory,
                options,
                bones,
                boneIndicesByMOWName,
                usedBoneNames,
                meshes,
                materials,
                textureResolver);
        }
    }

    private static Mesh ConvertMesh(
        MOWPlyFile ply,
        Matrix4x4 transform,
        int boneIndex,
        ModelParseOptions options,
        string modelDirectory,
        Dictionary<string, Material> materials,
        MOWTextureResolver textureResolver)
    {
        var vertices = ply.Vertices
            .Select(vertex => ConvertVertex(vertex, transform, boneIndex, options))
            .ToArray();
        var submeshes = ply.Submeshes
            .Select(submesh => new Submesh(
                LoadMaterial(submesh.MaterialFile, modelDirectory, materials, textureResolver),
                submesh.Triangles))
            .ToArray();

        return new Mesh(vertices, submeshes);
    }

    private static IReadOnlyList<AnimationClip> LoadAnimations(
        MOWNode root,
        string modelDirectory,
        IReadOnlyList<Bone> bones,
        IReadOnlyDictionary<string, List<int>> boneIndicesByMOWName,
        ModelParseOptions options,
        ILogger logger)
    {
        if (bones.Count == 0)
        {
            return [];
        }

        List<AnimationClip> clips = [];

        foreach (var sequence in root.Descendants("sequence"))
        {
            var sequenceName = GetSequenceName(sequence);
            if (string.IsNullOrWhiteSpace(sequenceName))
            {
                continue;
            }

            var animationPath = ResolveAnimationPath(sequence, sequenceName, modelDirectory);
            if (!File.Exists(animationPath))
            {
                logger.MissingAnimationFile(sequenceName, animationPath);
                continue;
            }

            var animation = MOWAnimationFile.Read(animationPath);
            var clip = BuildAnimationClip(sequenceName, animation, bones, boneIndicesByMOWName, options);
            if (clip.Tracks.Count > 0)
            {
                clips.Add(clip);
            }
        }

        return clips;
    }

    private static AnimationClip BuildAnimationClip(
        string name,
        MOWAnimationFile animation,
        IReadOnlyList<Bone> bones,
        IReadOnlyDictionary<string, List<int>> boneIndicesByMOWName,
        ModelParseOptions options)
    {
        Dictionary<int, Transform> currentTransforms = bones.ToDictionary(bone => bone.Index, bone => bone.LocalBindPose);
        Dictionary<int, Dictionary<ushort, TransformKeyframe>> keyframesByBone = [];

        foreach (var frame in animation.Frames.OrderBy(frame => frame.Time))
        {
            foreach (var frameEvent in frame.Events)
            {
                if (frameEvent.EntityIndex < 0 || frameEvent.EntityIndex >= animation.Entities.Count)
                {
                    continue;
                }

                var entityName = animation.Entities[frameEvent.EntityIndex];
                if (!boneIndicesByMOWName.TryGetValue(entityName, out var boneIndices))
                {
                    continue;
                }

                foreach (var boneIndex in boneIndices)
                {
                    var current = currentTransforms[boneIndex];
                    var transform = new Transform(
                        frameEvent.Position.HasValue
                            ? TransformAnimationPosition(frameEvent.Position.Value, options)
                            : current.Translation,
                        frameEvent.Rotation.HasValue
                            ? TransformRotation(frameEvent.Rotation.Value, options)
                            : current.Rotation,
                        current.Scale);
                    currentTransforms[boneIndex] = transform;

                    if (!keyframesByBone.TryGetValue(boneIndex, out var keyframes))
                    {
                        keyframes = [];
                        keyframesByBone[boneIndex] = keyframes;
                    }

                    keyframes[frame.Time] = new TransformKeyframe(frame.Time / AnimationFrameRate, transform);
                }
            }
        }

        var tracks = keyframesByBone
            .OrderBy(pair => pair.Key)
            .Select(pair => new BoneTransformTrack(
                pair.Key,
                pair.Value.OrderBy(keyframe => keyframe.Key).Select(keyframe => keyframe.Value).ToArray()))
            .Where(track => track.Keyframes.Count > 0)
            .Cast<IAnimationTrack>()
            .ToArray();
        var maxFrame = animation.Frames.Count == 0 ? 0 : animation.Frames.Max(frame => frame.Time);
        var durationFrame = Math.Max(maxFrame, animation.DurationFrames > 0 ? animation.DurationFrames - 1 : 0);

        return new AnimationClip(name, AnimationFrameRate, durationFrame / AnimationFrameRate, tracks);
    }

    private static string? GetSequenceName(MOWNode sequence)
    {
        return sequence.Values.Count > 0 ? sequence.Values[0] : null;
    }

    private static string ResolveAnimationPath(MOWNode sequence, string sequenceName, string modelDirectory)
    {
        var fileName = sequence.FirstChild("file")?.Values.FirstOrDefault();
        fileName = string.IsNullOrWhiteSpace(fileName) ? $"{sequenceName}.anm" : fileName;

        return Path.GetFullPath(Path.IsPathRooted(fileName) ? fileName : Path.Combine(modelDirectory, fileName));
    }

    private static Vertex ConvertVertex(Vertex vertex, Matrix4x4 transform, int boneIndex, ModelParseOptions options)
    {
        var position = Vector3.Transform(vertex.Position, transform) * options.ScaleFactor;
        var normal = Vector3.TransformNormal(vertex.Normal, transform);
        normal = normal.LengthSquared() <= 0.000001f ? Vector3.UnitZ : Vector3.Normalize(normal);

        return new Vertex(
            ModelAxisTransforms.TransformPosition(position, options.AxisMode, "mow"),
            ModelAxisTransforms.TransformNormal(normal, options.AxisMode, "mow"),
            vertex.TextureCoordinate,
            [new VertexBoneWeight(boneIndex, 1.0f)]);
    }

    private static string LoadMaterial(
        string plyMaterialFile,
        string modelDirectory,
        Dictionary<string, Material> materials,
        MOWTextureResolver textureResolver)
    {
        var materialFileName = string.IsNullOrWhiteSpace(plyMaterialFile) ? "default.mtl" : plyMaterialFile;
        var materialPath = Path.GetFullPath(Path.Combine(modelDirectory, materialFileName));
        var materialName = NameHelpers.SanitizeMaterialName(Path.GetFileNameWithoutExtension(materialFileName));

        if (materials.ContainsKey(materialName))
        {
            return materialName;
        }

        if (!File.Exists(materialPath))
        {
            textureResolver.Logger.MissingMaterialFile(materialPath);
            materials[materialName] = new Material(materialName);
            return materialName;
        }

        var materialFile = MOWMaterialFile.Read(materialPath);
        materials[materialName] = new Material(
            materialName,
            diffuseTexture: textureResolver.LoadTexture(materialFile.DiffuseTexture, hasAlpha: materialFile.UsesAlpha),
            specularTexture: textureResolver.LoadTexture(materialFile.SpecularTexture, hasAlpha: false),
            normalTexture: textureResolver.LoadTexture(materialFile.NormalTexture, hasAlpha: false));
        return materialName;
    }

    private static IReadOnlyList<string> GetMeshFileNames(MOWNode bone)
    {
        var directVolumeViews = bone.Children
            .Where(IsVolumeViewNode)
            .Where(node => node.Values.Count > 0)
            .ToArray();
        if (directVolumeViews.Length > 0)
        {
            return directVolumeViews.Select(node => node.Values[0]).ToArray();
        }

        var lodView = bone.FirstChild("LodView");
        if (lodView is null)
        {
            return [];
        }

        return lodView.Children
            .Where(IsVolumeViewNode)
            .Where(node => node.Values.Count > 0)
            .Select(node => node.Values[0])
            .ToArray();
    }

    private static Matrix4x4 GetLocalTransform(MOWNode bone)
    {
        var matrix34 = bone.FirstChild("Matrix34");
        if (matrix34 is not null)
        {
            return ParseMatrix34(matrix34);
        }

        var orientation = bone.FirstChild("Orientation");
        var position = bone.FirstChild("Position");
        var rotation = orientation is null ? Matrix4x4.Identity : ParseOrientation(orientation);
        var translation = position is null ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(ParseVector3(position.Values));
        return rotation * translation;
    }

    private static string GetMOWBoneName(MOWNode bone, int index)
    {
        var name = bone.Values.LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return !string.IsNullOrWhiteSpace(name)
            ? name
            : FormattableString.Invariant($"bone_{index}");
    }

    private static string MakeUniqueBoneName(string boneName, HashSet<string> usedBoneNames)
    {
        var baseName = string.IsNullOrWhiteSpace(boneName) ? "bone" : boneName.Trim();
        var candidate = baseName;
        var suffix = 2;
        while (!usedBoneNames.Add(candidate))
        {
            candidate = FormattableString.Invariant($"{baseName}_{suffix}");
            suffix++;
        }

        return candidate;
    }

    private static void AddBoneNameLookup(Dictionary<string, List<int>> lookup, string boneName, int boneIndex)
    {
        if (!lookup.TryGetValue(boneName, out var indices))
        {
            indices = [];
            lookup[boneName] = indices;
        }

        indices.Add(boneIndex);
    }

    private static Transform ConvertBoneTransform(Matrix4x4 localTransform, ModelParseOptions options)
    {
        if (!Matrix4x4.Decompose(localTransform, out var scale, out var rotation, out var translation))
        {
            scale = Vector3.One;
            rotation = Quaternion.Identity;
            translation = localTransform.Translation;
        }

        translation = ModelAxisTransforms.TransformPosition(translation * options.ScaleFactor, options.AxisMode, "mow");
        scale = ModelAxisTransforms.TransformScale(scale, options.AxisMode, "mow");
        rotation = TransformRotation(rotation, options);

        return new Transform(translation, rotation, scale);
    }

    private static Vector3 TransformAnimationPosition(Vector3 position, ModelParseOptions options)
    {
        return ModelAxisTransforms.TransformPosition(position * options.ScaleFactor, options.AxisMode, "mow");
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

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        return rotation.LengthSquared() <= 0.000001f ? Quaternion.Identity : Quaternion.Normalize(rotation);
    }

    private static Matrix4x4 ParseMatrix34(MOWNode node)
    {
        if (node.Values.Count < 12)
        {
            throw new GMConverterException("Men of War Matrix34 node must contain 12 values.");
        }

        var values = node.Values.Select(ParseFloat).ToArray();
        return FromRows(
            values[0], values[1], values[2],
            values[3], values[4], values[5],
            values[6], values[7], values[8],
            values[9], values[10], values[11]);
    }

    private static Matrix4x4 ParseOrientation(MOWNode node)
    {
        if (node.Values.Count < 9)
        {
            throw new GMConverterException("Men of War Orientation node must contain 9 values.");
        }

        var values = node.Values.Select(ParseFloat).ToArray();
        return FromRows(
            values[0], values[1], values[2],
            values[3], values[4], values[5],
            values[6], values[7], values[8],
            0, 0, 0);
    }

    private static Matrix4x4 FromRows(
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33,
        float tx, float ty, float tz)
    {
        return new Matrix4x4(
            m11, m21, m31, 0,
            m12, m22, m32, 0,
            m13, m23, m33, 0,
            tx, ty, tz, 1);
    }

    private static Vector3 ParseVector3(IReadOnlyList<string> values)
    {
        if (values.Count < 3)
        {
            throw new GMConverterException("Men of War vector node must contain 3 values.");
        }

        return new Vector3(ParseFloat(values[0]), ParseFloat(values[1]), ParseFloat(values[2]));
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static bool IsBoneNode(MOWNode node)
    {
        return string.Equals(node.Name, "bone", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVolumeViewNode(MOWNode node)
    {
        return string.Equals(node.Name, "VolumeView", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MOWTextureResolver
    {
        private static readonly string[] ImageExtensions = [".dds", ".png", ".tga", ".bmp", ".jpg", ".jpeg"];
        private readonly string modelDirectory;
        private readonly Dictionary<string, string> searchIndex;

        private MOWTextureResolver(string modelDirectory, Dictionary<string, string> searchIndex, ILogger logger)
        {
            this.modelDirectory = modelDirectory;
            this.searchIndex = searchIndex;
            Logger = logger;
        }

        public ILogger Logger { get; }

        public static MOWTextureResolver Create(
            string modelDirectory,
            MaterialResolveOptions? options,
            ILoggerFactory loggerFactory)
        {
            var searchIndex = options is null ||
                string.IsNullOrWhiteSpace(options.SearchDirectory) ||
                !Directory.Exists(options.SearchDirectory)
                    ? []
                    : IndexFiles(options.SearchDirectory);

            return new MOWTextureResolver(
                modelDirectory,
                searchIndex,
                loggerFactory.CreateLogger<MOWTextureResolver>());
        }

        public Texture? LoadTexture(string? textureName, bool hasAlpha)
        {
            if (string.IsNullOrWhiteSpace(textureName))
            {
                return null;
            }

            var texturePath = ResolveTexturePath(textureName);
            if (texturePath is null)
            {
                Logger.MissingTextureReference(textureName);
                return null;
            }

            var image = new MagickImage(texturePath);
            if (!hasAlpha && image.HasAlpha)
            {
                image.Alpha(AlphaOption.Opaque);
            }

            return new Texture(NameHelpers.SanitizeMaterialName(Path.GetFileNameWithoutExtension(texturePath)), image, hasAlpha);
        }

        private string? ResolveTexturePath(string textureName)
        {
            foreach (var localCandidate in LocalCandidates(textureName))
            {
                if (File.Exists(localCandidate))
                {
                    return localCandidate;
                }
            }

            return searchIndex.GetValueOrDefault(NameHelpers.SanitizeMaterialName(Path.GetFileNameWithoutExtension(textureName)));
        }

        private IEnumerable<string> LocalCandidates(string textureName)
        {
            var normalized = textureName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
            {
                yield return normalized;
            }
            else
            {
                yield return Path.GetFullPath(Path.Combine(modelDirectory, normalized));
            }

            if (!Path.HasExtension(normalized))
            {
                foreach (var extension in ImageExtensions)
                {
                    yield return Path.GetFullPath(Path.Combine(modelDirectory, $"{normalized}{extension}"));
                }
            }
        }

        private static Dictionary<string, string> IndexFiles(string directory)
        {
            Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .OrderBy(ImageExtensionPriority)
                         .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                index.TryAdd(NameHelpers.SanitizeMaterialName(Path.GetFileNameWithoutExtension(file)), file);
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
