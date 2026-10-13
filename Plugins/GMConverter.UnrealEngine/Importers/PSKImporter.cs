using System.Numerics;
using System.Text.Json;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;
using GMConverter.SDK.Physics;
using GMConverter.SDK.Textures;
using GMConverter.UnrealEngine.Common;
using GMConverter.UnrealEngine.Formats.PSA;
using GMConverter.UnrealEngine.Formats.PSK;
using GMConverter.UnrealEngine.Formats.Unreal;
using SixLabors.ImageSharp;

namespace GMConverter.UnrealEngine.Importers;

internal sealed class PSKImporter : IModelImporter
{
    private readonly ITextureFactory _textureFactory;

    public PSKImporter(ITextureFactory textureFactory)
    {
        _textureFactory = textureFactory;
    }

    public string Format => "psk";

    public string DisplayName => "Unreal Engine";

    public IReadOnlyList<string> FileExtensions { get; } = [".psk", ".pskx", ".ue4scene"];

    public OptionSchema OptionSchema { get; } = new(
    [
        new OptionGroup("animation", "Animation",
        [
            new OptionDescriptor(PSKImporterOptions.AnimationPath, OptionType.Path, "Animation path")
            {
                Description = "Optional PSA animation files to import alongside the mesh. Separate several files with ';'.",
                Aliases = ["animation-path"],
                AllowsMultiple = true,
            },
        ]),
    ]);

    public object Summarize(string inputPath)
    {
        if (IsSceneManifest(inputPath))
        {
            var manifest = ReadSceneManifest(inputPath);
            return $"Unreal scene with {manifest.Entries.Count} mesh part(s).";
        }

        return PSKSummary.From(inputPath, PSKFile.Read(inputPath));
    }

    public Model Import(ModelImportContext context) => Parse(context.InputPath, context);

    private Model Parse(string inputPath, ModelImportContext options)
    {
        using var scope = PerfTimer.Measure("psk.import", "Parse", inputPath);
        // ActorX geometry, bone positions, animation translations, and scene offsets use
        // centimeters. Normalize once at the import boundary; exporters consume meters.
        options = options with { ScaleFactor = options.ScaleFactor * 0.01f };
        return IsSceneManifest(inputPath) ? ParseScene(inputPath, options) : ParseSingle(inputPath, options, null);
    }

    private Model ParseScene(string inputPath, ModelImportContext options)
    {
        using var sceneScope = PerfTimer.Measure("psk.import", "ParseScene", inputPath);
        var manifest = ReadSceneManifest(inputPath);
        PerfTimer.Log("psk.import", $"manifest entries={manifest.Entries.Count}");

        var materialResolver = PSKMaterialResolver.Create(options.Materials, _textureFactory);

        // Parse entries in parallel — each ParseSingle reads its own PSK file from disk, builds
        // its own Mesh/Material/Texture objects, and only reads from the shared materialResolver
        // (whose internal dictionaries are read-only after construction). Index-keyed buffer
        // preserves manifest order so the resulting meshes/materials are deterministic.
        var sceneDirectory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var parsedModels = new Model[manifest.Entries.Count];
        Parallel.For(0, manifest.Entries.Count, entryIndex =>
        {
            var entry = manifest.Entries[entryIndex];
            // Manifest entry paths are produced by ExportResolvedScene via Path.GetRelativePath,
            // so a well-formed manifest only ever contains relative paths under sceneDirectory.
            // Run them through PathHelpers.TryResolveUnderRoot anyway — a tampered or corrupted
            // manifest could otherwise let a rooted entry.Path silently replace sceneDirectory
            // and walk the importer through arbitrary filesystem locations.
            if (!PathHelpers.TryResolveUnderRoot(sceneDirectory, entry.Path, out var entryPath))
            {
                throw new GMConverterException(
                    $"Unreal scene manifest entry references a path outside the scene directory: '{entry.Path}'.");
            }
            using var entryScope = PerfTimer.Measure(
                "psk.import",
                "ParseScene.Entry",
                $"{entryIndex + 1}/{manifest.Entries.Count} {Path.GetFileName(entryPath)}");
            parsedModels[entryIndex] = ParseSingle(entryPath, options, entry.Transform, materialResolver, buildAnimations: false);
        });

        // Skeletal parts each carry their own copy of the skeleton; merge them so the scene keeps
        // its rig and the requested animation binds once against the combined bone list.
        var (skeleton, parts) = PSKSceneSkeleton.Merge(parsedModels);

        List<Mesh> meshes = [];
        Dictionary<string, Material> materials = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, Material>> skins = new(StringComparer.OrdinalIgnoreCase);
        List<Attachment> attachments = [];
        HashSet<string> attachmentNames = new(StringComparer.OrdinalIgnoreCase);
        List<PhysicsBody> bodies = [];
        List<PhysicsJoint> joints = [];
        foreach (var model in parts)
        {
            meshes.AddRange(model.Meshes);

            // Parts that share a skeleton usually share its physics asset too; keep one body and
            // one joint per bone.
            bodies.AddRange(model.Physics?.Bodies.Where(body => bodies.All(existing => existing.BoneIndex != body.BoneIndex)) ?? []);
            joints.AddRange(model.Physics?.Joints.Where(joint => joints.All(existing => existing.BoneIndex != joint.BoneIndex)) ?? []);
            foreach (var attachment in model.Attachments ?? [])
            {
                // Repeated parts (the same prop placed twice) repeat their sockets too.
                var name = attachment.Name;
                for (var suffix = 2; !attachmentNames.Add(name); suffix++)
                {
                    name = FormattableString.Invariant($"{attachment.Name}_{suffix}");
                }

                attachments.Add(attachment with { Name = name });
            }

            foreach (var material in model.Materials)
            {
                materials.TryAdd(material.Name, material);
            }

            foreach (var skin in model.Skins ?? [])
            {
                if (!skins.TryGetValue(skin.Name, out var replacements))
                {
                    replacements = new(StringComparer.OrdinalIgnoreCase);
                    skins.Add(skin.Name, replacements);
                }

                foreach (var (materialName, replacement) in skin.Replacements)
                {
                    replacements.TryAdd(materialName, replacement);
                }
            }
        }

        if (meshes.Count == 0)
        {
            throw new GMConverterException("Unreal scene manifest did not contain any readable mesh parts.");
        }

        return new Model(
            string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(inputPath) : manifest.Name,
            meshes,
            materials.Count == 0 ? [new Material("default")] : materials.Values.ToArray(),
            skeleton,
            BuildAnimations(skeleton, options),
            Skins: skins.Count == 0 ? null : [.. skins.Select(skin => new MaterialSkin(skin.Key, skin.Value))],
            Attachments: attachments.Count == 0 ? null : attachments,
            Physics: bodies.Count == 0 ? null : new ModelPhysics(bodies, joints));
    }

    private Model ParseSingle(string inputPath, ModelImportContext options, PSKSceneTransform? sceneTransform)
    {
        return ParseSingle(inputPath, options, sceneTransform, PSKMaterialResolver.Create(options.Materials, _textureFactory));
    }

    private static Model ParseSingle(
        string inputPath,
        ModelImportContext options,
        PSKSceneTransform? sceneTransform,
        PSKMaterialResolver materialResolver,
        bool buildAnimations = true)
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

        // When the SCS scene transform has an odd number of negative scale components (e.g.
        // mirrored variants with relScale=(-1,1,1) like Side_R), every triangle's winding gets
        // reversed by the mirror. Without compensating, Source treats the mirrored mesh's outside
        // as inside — visible as inverted lighting (the lit side is the one in shadow). Flip the
        // triangle index order so the post-mirror winding lands on the correct face direction.
        var hasNegativeScale = sceneTransform is not null &&
            sceneTransform.Scale.X * sceneTransform.Scale.Y * sceneTransform.Scale.Z < 0f;

        // Closed shells shipped inside-out relative to the rest of the mesh are read with their
        // corners in the opposite order, which also flips the face normal derived from them.
        var invertedFaces = PSKShellWinding.FindInvertedFaces(psk);

        for (var faceIndex = 0; faceIndex < psk.Faces.Count; faceIndex++)
        {
            var face = psk.Faces[faceIndex];
            var (first, last) = invertedFaces.Contains(faceIndex) ? (0, 2) : (2, 0);
            if (!TryGetCorner(psk, face.WedgeIndices[first], weightLookup, options, sceneTransform, out var a) ||
                !TryGetCorner(psk, face.WedgeIndices[1], weightLookup, options, sceneTransform, out var b) ||
                !TryGetCorner(psk, face.WedgeIndices[last], weightLookup, options, sceneTransform, out var c))
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
            triangles.Add(hasNegativeScale
                ? new Triangle(vertexOffset, vertexOffset + 2, vertexOffset + 1)
                : new Triangle(vertexOffset, vertexOffset + 1, vertexOffset + 2));
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

        return new Model(
            modelName,
            [mesh],
            materials,
            skeleton,
            buildAnimations ? BuildAnimations(skeleton, options) : null,
            BuildSkins(distinctMaterials, resolvedMaterials, materialResolver, modelName, inputPath),
            BuildAttachments(inputPath, skeleton, options, sceneTransform),
            BuildPhysics(inputPath, skeleton, options));
    }

    // Bodies and joints are relative to their bones, so like bone sockets they need no scene
    // placement. Bodies on bones this mesh doesn't have are dropped, and so are joints whose
    // bodies are gone.
    private static ModelPhysics? BuildPhysics(string inputPath, Skeleton? skeleton, ModelImportContext options)
    {
        if (skeleton is null || PSKPhysicsSidecar.TryRead(inputPath) is not { Bodies.Count: > 0 } sidecar)
        {
            return null;
        }

        Dictionary<string, int> boneIndices = new(StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            boneIndices.TryAdd(NormalizeBoneName(bone.Name), bone.Index);
        }

        int BoneIndexOf(string name) => boneIndices.GetValueOrDefault(NormalizeBoneName(name), -1);

        List<PhysicsBody> bodies =
        [
            .. sidecar.Bodies
                .Select(body => (Body: body, BoneIndex: BoneIndexOf(body.Bone)))
                .Where(item => item.BoneIndex >= 0)
                .DistinctBy(item => item.BoneIndex)
                .Select(item => new PhysicsBody(item.BoneIndex, [.. item.Body.Shapes.Select(shape => BuildCollisionShape(shape, options))])),
        ];
        var bodyBones = bodies.Select(body => body.BoneIndex).ToHashSet();

        // Y-up swaps the frame's Y and Z axes along with everything else.
        var isYUp = options.AxisMode is ModelAxisMode.YUp;
        List<PhysicsJoint> joints =
        [
            .. sidecar.Joints
                .Select(joint => (Joint: joint, BoneIndex: BoneIndexOf(joint.Bone), ParentBoneIndex: BoneIndexOf(joint.ParentBone)))
                .Where(item => bodyBones.Contains(item.BoneIndex) && bodyBones.Contains(item.ParentBoneIndex))
                .DistinctBy(item => item.BoneIndex)
                .Select(item =>
                {
                    var swing1 = ToAngularLimit(item.Joint.Swing1Limit);
                    var swing2 = ToAngularLimit(item.Joint.Swing2Limit);
                    return new PhysicsJoint(
                        item.BoneIndex,
                        item.ParentBoneIndex,
                        TransformRotation(item.Joint.Frame, options),
                        ToAngularLimit(item.Joint.TwistLimit),
                        isYUp ? swing1 : swing2,
                        isYUp ? swing2 : swing1);
                }),
        ];

        return bodies.Count == 0 ? null : new ModelPhysics(bodies, joints);
    }

    private static AngularLimit? ToAngularLimit(float? halfAngle)
    {
        return halfAngle is { } degrees ? new AngularLimit(-degrees, degrees) : null;
    }

    private static CollisionShape BuildCollisionShape(PSKPhysicsShape shape, ModelImportContext options)
    {
        var rotation = TransformRotation(shape.Rotation, options);
        var transform = new Transform(TransformPosition(shape.Center, options), rotation, Vector3.One);
        switch (shape.Kind)
        {
            case PSKPhysicsShapeKind.Sphere:
                return new SphereShape(transform, shape.Radius * options.ScaleFactor);
            case PSKPhysicsShapeKind.Box:
                return new BoxShape(transform, TransformScale(shape.HalfExtents, options) * options.ScaleFactor);
            case PSKPhysicsShapeKind.Capsule:
                // Y-up maps the capsule's Z axis to the shape's Y axis; turn it back onto Z.
                if (options.AxisMode is ModelAxisMode.YUp)
                {
                    transform = transform with { Rotation = rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f) };
                }

                return new CapsuleShape(transform, shape.Radius * options.ScaleFactor, shape.Length * options.ScaleFactor);
            default:
                return new ConvexShape(transform, [.. (shape.Points ?? []).Select(point => TransformPosition(point, options))]);
        }
    }

    // Sockets on a bone stay relative to it; sockets on a static mesh are relative to the mesh,
    // so they take the part's scene placement like its vertices do.
    private static List<Attachment>? BuildAttachments(
        string inputPath,
        Skeleton? skeleton,
        ModelImportContext options,
        PSKSceneTransform? sceneTransform)
    {
        if (PSKSocketSidecar.TryRead(inputPath) is not { Sockets.Count: > 0 } sidecar)
        {
            return null;
        }

        List<Attachment> attachments = [];
        foreach (var socket in sidecar.Sockets)
        {
            if (socket.Bone is null)
            {
                var location = sceneTransform?.TransformPosition(socket.Location) ?? socket.Location;
                var rotation = sceneTransform is null ? socket.Rotation : sceneTransform.Rotation.ToQuaternion() * socket.Rotation;
                attachments.Add(new Attachment(
                    socket.Name,
                    null,
                    new Transform(TransformPosition(location, options), TransformRotation(rotation, options), Vector3.One)));
                continue;
            }

            // A socket on a bone this mesh doesn't have can't be placed.
            var bone = skeleton?.Bones.FirstOrDefault(bone =>
                NormalizeBoneName(bone.Name).Equals(NormalizeBoneName(socket.Bone), StringComparison.OrdinalIgnoreCase));
            if (bone is not null)
            {
                attachments.Add(new Attachment(
                    socket.Name,
                    bone.Index,
                    new Transform(TransformPosition(socket.Location, options), TransformRotation(socket.Rotation, options), Vector3.One)));
            }
        }

        return attachments.Count == 0 ? null : attachments;
    }

    // Each material's sidecar variants become skins named by what follows the shared stem
    // (bddispenser_off -> bddispenser_on is skin "on"), so variants of several materials that
    // share a suffix land in the same skin.
    private static IReadOnlyList<MaterialSkin>? BuildSkins(
        PSKMaterial[] meshMaterials,
        Material[] resolvedMaterials,
        PSKMaterialResolver materialResolver,
        string meshName,
        string meshPath)
    {
        Dictionary<string, Dictionary<string, Material>> skins = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < meshMaterials.Length; i++)
        {
            var baseName = resolvedMaterials[i].Name;
            var stemLength = baseName.LastIndexOf('_') + 1;
            foreach (var variant in materialResolver.ResolveVariants(meshMaterials[i], meshName, meshPath))
            {
                var skinName = stemLength > 0 && variant.Name.Length > stemLength &&
                    variant.Name.StartsWith(baseName[..stemLength], StringComparison.OrdinalIgnoreCase)
                    ? variant.Name[stemLength..]
                    : variant.Name;
                if (!skins.TryGetValue(skinName, out var replacements))
                {
                    replacements = new(StringComparer.OrdinalIgnoreCase);
                    skins.Add(skinName, replacements);
                }

                replacements.TryAdd(baseName, variant);
            }
        }

        return skins.Count == 0 ? null : [.. skins.Select(skin => new MaterialSkin(skin.Key, skin.Value))];
    }

    private static bool TryGetCorner(
        PSKFile psk,
        int wedgeIndex,
        IReadOnlyDictionary<int, IReadOnlyList<VertexBoneWeight>> weightLookup,
        ModelImportContext options,
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

    private static Skeleton? BuildSkeleton(PSKFile psk, ModelImportContext options)
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

    private static List<AnimationClip>? BuildAnimations(Skeleton? skeleton, ModelImportContext options)
    {
        var animationPaths = options.Options.GetStrings(PSKImporterOptions.AnimationPath);
        if (skeleton is null || animationPaths.Count == 0)
        {
            return null;
        }

        // Every bundled PSA becomes its own set of clips. UE5 exports one sequence per file, so
        // names rarely collide, but a repeated name would make exporters drop or overwrite one.
        List<AnimationClip> clips = [];
        HashSet<string> clipNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<(string Base, int Frame), AnimationPose> basePoses = [];
        foreach (var animationOption in animationPaths)
        {
            foreach (var clip in ReadAnimationClips(skeleton, animationOption, options, basePoses))
            {
                var name = clip.Name;
                for (var suffix = 2; !clipNames.Add(name); suffix++)
                {
                    name = FormattableString.Invariant($"{clip.Name}_{suffix}");
                }

                clips.Add(clip with { Name = name });
            }
        }

        return clips;
    }

    private static List<AnimationClip> ReadAnimationClips(
        Skeleton skeleton,
        string animationOption,
        ModelImportContext options,
        Dictionary<(string Base, int Frame), AnimationPose> basePoses)
    {
        var animationPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(animationOption));
        if (!File.Exists(animationPath))
        {
            throw new GMConverterException($"Animation file not found: {animationPath}");
        }

        if (!string.Equals(Path.GetExtension(animationPath), ".psa", StringComparison.OrdinalIgnoreCase))
        {
            throw new GMConverterException($"Expected a .psa animation file: {animationPath}");
        }

        var psa = PSAFile.Read(animationPath);
        if (psa.Bones.Count == 0 || psa.Sequences.Count == 0)
        {
            return [];
        }

        var psaBoneToSkeletonBone = MapPsaBones(skeleton, psa);
        var layering = ReadLayering(skeleton, animationPath, options, basePoses);
        var playback = PSAClipSidecar.TryRead(animationPath);
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
                    var transform = ReadBoneTransform(skeleton, skeletonBoneIndex, keys, scaleKeys, keyIndex, options);
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
                tracks,
                layering,
                RootMotion: playback?.RootMotion ?? false,
                Loops: playback?.Loops ?? false));
        }

        return clips;
    }

    private static int[] MapPsaBones(Skeleton skeleton, PSAFile psa)
    {
        var boneNameToIndex = skeleton.Bones
            .GroupBy(bone => NormalizeBoneName(bone.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        return [.. psa.Bones.Select(bone => boneNameToIndex.TryGetValue(NormalizeBoneName(bone.Name), out var boneIndex) ? boneIndex : -1)];
    }

    private static Transform ReadBoneTransform(
        Skeleton skeleton,
        int skeletonBoneIndex,
        IReadOnlyList<PSAKey> keys,
        IReadOnlyList<PSAScaleKey> scaleKeys,
        int keyIndex,
        ModelImportContext options)
    {
        var key = keys[keyIndex];
        var scale = scaleKeys.Count == keys.Count
            ? TransformScale(scaleKeys[keyIndex].Scale, options)
            : Vector3.One;
        return new Transform(
            TransformPosition(key.Location, options),
            TransformBoneRotation(key.Rotation, options, skeleton.Bones[skeletonBoneIndex].ParentIndex >= 0),
            scale);
    }

    // The explorer leaves a sidecar next to additive animations naming the pose they were authored
    // against. Clips that share a base share one AnimationPose, so exporters can write it once.
    private static AnimationLayering? ReadLayering(
        Skeleton skeleton,
        string animationPath,
        ModelImportContext options,
        Dictionary<(string Base, int Frame), AnimationPose> basePoses)
    {
        if (PSALayerSidecar.TryRead(animationPath) is not { } sidecar)
        {
            return null;
        }

        if (sidecar.ResolveBasePath(animationPath) is not { } basePath)
        {
            return new AnimationLayering(sidecar.Space);
        }

        if (!basePoses.TryGetValue((sidecar.BaseId ?? basePath, sidecar.BaseFrame), out var basePose))
        {
            if (!File.Exists(basePath) || ReadPose(skeleton, basePath, sidecar.BaseFrame, options) is not { } pose)
            {
                // Without its base the layer can't be recovered; the baked clip still plays as-is.
                return null;
            }

            basePose = pose;
            basePoses.Add((sidecar.BaseId ?? basePath, sidecar.BaseFrame), basePose);
        }

        return new AnimationLayering(sidecar.Space, basePose);
    }

    private static AnimationPose? ReadPose(Skeleton skeleton, string psaPath, int frame, ModelImportContext options)
    {
        var psa = PSAFile.Read(psaPath);
        if (psa.Bones.Count == 0 || psa.Sequences.Count == 0)
        {
            return null;
        }

        var sequence = psa.Sequences[0];
        var keys = psa.GetSequenceKeys(sequence);
        var scaleKeys = psa.GetSequenceScaleKeys(sequence);
        var frameIndex = Math.Clamp(frame, 0, Math.Max(0, sequence.FrameCount - 1));
        var psaBoneToSkeletonBone = MapPsaBones(skeleton, psa);
        Dictionary<int, Transform> transforms = [];
        for (var psaBoneIndex = 0; psaBoneIndex < psa.Bones.Count; psaBoneIndex++)
        {
            var skeletonBoneIndex = psaBoneToSkeletonBone[psaBoneIndex];
            if (skeletonBoneIndex >= 0)
            {
                var keyIndex = checked(frameIndex * psa.Bones.Count + psaBoneIndex);
                transforms.TryAdd(skeletonBoneIndex, ReadBoneTransform(skeleton, skeletonBoneIndex, keys, scaleKeys, keyIndex, options));
            }
        }

        return new AnimationPose(FormattableString.Invariant($"{sequence.Name}_{frameIndex}"), transforms);
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

    private static Vector3 TransformPosition(Vector3 position, ModelImportContext options)
    {
        var scaled = position * options.ScaleFactor;
        return ModelAxisTransforms.TransformPosition(scaled, options.AxisMode, "psk");
    }

    private static Vector3 TransformNormal(Vector3 normal, ModelImportContext options)
    {
        return ModelAxisTransforms.TransformNormal(normal, options.AxisMode, "psk");
    }

    private static Vector3 TransformScale(Vector3 scale, ModelImportContext options)
    {
        return ModelAxisTransforms.TransformScale(scale, options.AxisMode, "psk");
    }

    private static Quaternion TransformRotation(Quaternion rotation, ModelImportContext options)
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

    private static Quaternion TransformBoneRotation(Quaternion rotation, ModelImportContext options, bool hasParent)
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
            // For a normal direction the correct transform is the inverse-transpose of the
            // composed (scale × rotation). For a diagonal SCS scale that simplifies to "divide
            // each component by the per-axis scale" — and for the ±1 mirrors we see in Fortnite
            // SCS data, that's a per-axis sign flip. Without this, mirrored meshes (scale.X=-1
            // etc.) keep their inward-pointing normals after the position mirror, which makes
            // their lit side dark and shadow side bright. Combined with the triangle winding
            // flip in the face loop, this restores proper outward-facing normals.
            var s = Scale.ToVector3();
            var scaled = new Vector3(
                MathF.Abs(s.X) > 0.000001f ? normal.X / s.X : 0f,
                MathF.Abs(s.Y) > 0.000001f ? normal.Y / s.Y : 0f,
                MathF.Abs(s.Z) > 0.000001f ? normal.Z / s.Z : 0f);
            return Vector3.Transform(scaled, Rotation.ToQuaternion());
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
        private readonly ITextureFactory _textureFactory;

        private PSKMaterialResolver(
            Dictionary<string, string> materialSidecars,
            Dictionary<string, string> cueMaterialSidecars,
            Dictionary<string, string> images,
            ITextureFactory textureFactory)
        {
            _materialSidecars = materialSidecars;
            _cueMaterialSidecars = cueMaterialSidecars;
            _images = images;
            _textureFactory = textureFactory;
        }

        public static PSKMaterialResolver Create(MaterialResolveOptions? options, ITextureFactory textureFactory)
        {
            if (options is null || string.IsNullOrWhiteSpace(options.SearchDirectory) ||
                !Directory.Exists(options.SearchDirectory))
            {
                return new PSKMaterialResolver([], [], [], textureFactory);
            }

            return new PSKMaterialResolver(
                IndexFiles(options.SearchDirectory, [".mat"]),
                IndexFiles(options.SearchDirectory, [".json"]),
                IndexFiles(options.SearchDirectory, _imageExtensions),
                textureFactory);
        }

        public Material Resolve(PSKMaterial material, string meshName, string meshPath)
        {
            Dictionary<string, string>? references = null;
            IReadOnlyDictionary<string, CueMaterialColor> colors = new Dictionary<string, CueMaterialColor>(StringComparer.OrdinalIgnoreCase);
            var usesCueMaterial = false;
            System.Numerics.Vector2? bakedUv0Scale = null;
            var sidecarSettings = SidecarMaterialSettings.Default;
            string? materialAlias = null;
            if (TryGetLocalSidecar(material, meshPath, ".json", out var localCueMaterialPath))
            {
                var cueMaterial = ReadCueMaterial(localCueMaterialPath);
                references = cueMaterial.Textures;
                colors = cueMaterial.Colors;
                bakedUv0Scale = cueMaterial.BakedUv0Scale;
                materialAlias = cueMaterial.MaterialAlias;
                sidecarSettings = cueMaterial.BlendSettings;
                usesCueMaterial = true;
            }
            else if (TryGetLocalSidecar(material, meshPath, ".mat", out var localMaterialPath))
            {
                references = ReadMaterialReferences(localMaterialPath);
                sidecarSettings = ReadSidecarSettings(localMaterialPath);
            }
            else if (TryGetSidecar(_materialSidecars, material, out var materialPath))
            {
                references = ReadMaterialReferences(materialPath);
                sidecarSettings = ReadSidecarSettings(materialPath);
            }
            else if (TryGetSidecar(_cueMaterialSidecars, material, out var cueMaterialPath))
            {
                var cueMaterial = ReadCueMaterial(cueMaterialPath);
                references = cueMaterial.Textures;
                colors = cueMaterial.Colors;
                bakedUv0Scale = cueMaterial.BakedUv0Scale;
                materialAlias = cueMaterial.MaterialAlias;
                sidecarSettings = cueMaterial.BlendSettings;
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
            MaterialEmissiveLayer? emissiveLayer = null;
            MaterialLayer? detailLayer = null;
            if (usesCueMaterial)
            {
                var materialContext = CreateMaterialContext(material.Name, meshName);
                // Fortnite supplies a separate Opacity texture; other games keep opacity in the
                // diffuse alpha, and only the material's blend mode says whether it matters.
                var diffuseHasAlpha = references.ContainsKey("Opacity") ||
                    sidecarSettings.BlendMode is MaterialBlendMode.AlphaTest or MaterialBlendMode.AlphaBlend;
                diffuseTexture = TryLoadCueMaterialTexture(materialContext, references, CueTextureKind.Diffuse, diffuseHasAlpha) ??
                    TryCreateColorTexture(material.Name, colors);
                if (!references.ContainsKey("Opacity") &&
                    sidecarSettings.BlendMode is MaterialBlendMode.AlphaTest &&
                    diffuseTexture is { HasAlpha: true } maskedDiffuse &&
                    !HasCutoutTexels(maskedDiffuse, out var maskedPixels))
                {
                    // Masked masters are also used for dithered fades and object hiding, where the
                    // diffuse alpha holds unrelated packed data. Alpha-testing that would punch holes
                    // in solid surfaces, so only honour the mask when the alpha has real cut-outs.
                    diffuseTexture = _textureFactory.FromRgba(
                        maskedDiffuse.Name,
                        maskedDiffuse.Width,
                        maskedDiffuse.Height,
                        maskedPixels,
                        hasAlpha: false,
                        maskedDiffuse.Path);
                    sidecarSettings = sidecarSettings with { BlendMode = MaterialBlendMode.Unspecified };
                }
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
                diffuseTexture = TryBlendDiffuseOverlay(diffuseTexture, references);
                detailLayer = TryLoadDetailLayer(references, sidecarSettings);
                normalTexture =
                    TryLoadLayerTexture(references, ["Normal", "NormalMap", "Normals"], layerSuffix, hasAlpha: false) ??
                    TryLoadTexture(references, ["Normal", "NormalMap", "Normals"], hasAlpha: false) ??
                    TryLoadTextureByName(references, ["normal", "norm", "nrm"], hasAlpha: false) ??
                    TryLoadRelatedTexture(references, ["Diffuse"], ["_normal", "_norm", "_bump"], hasAlpha: false);
                specularTexture = TryLoadLayerTexture(references, ["Specular", "SpecularityMask", "SpecularMasks"], layerSuffix, hasAlpha: false) ??
                    TryLoadTexture(references, ["Specular", "SpecularityMask", "SpecularMasks"], hasAlpha: false) ??
                    TryLoadTextureByName(references, ["spec", "rough", "metal", "orm", "mrao", "packed"], hasAlpha: false, excludedKeyTerms: ["norm", "nrm"]);
                // A SelfIlluminationMask confines the glow; if the masked bake can't be built, an
                // unmasked fallback would light the whole surface, so leave the material unlit.
                (emissiveTexture, emissiveLayer) = references.ContainsKey("SelfIlluminationMask")
                    ? TryBakeMaskedSelfIllumination(references, sidecarSettings)
                    : (TryLoadLayerTexture(references, ["Emissive", "SelfIllumination", "SelfIlluminationMask", "SFX_RGB"], layerSuffix, hasAlpha: false) ??
                    TryLoadTexture(references, ["Emissive", "SelfIllumination", "SelfIlluminationMask", "SFX_RGB"], hasAlpha: false) ??
                    TryLoadTextureByName(references, ["emiss", "sfx", "glow"], hasAlpha: false), null);
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
                    ? MaterialSpecularTexturePacking.SpecularMetallicRoughness
                    : MaterialSpecularTexturePacking.Standard,
                normalTextureConvention: usesCueMaterial
                    ? MaterialNormalTextureConvention.DirectX
                    : MaterialNormalTextureConvention.OpenGl,
                bakedUv0Scale: bakedUv0Scale,
                specularFactor: usesCueMaterial ? _fortniteSpecularFactor : 1.0f,
                uvScrollRate: sidecarSettings.UvScrollRate,
                emissiveUvScrollRate: sidecarSettings.EmissiveUvScrollRate,
                blendMode: sidecarSettings.BlendMode,
                alphaCutoff: sidecarSettings.AlphaCutoff,
                emissiveLayer: emissiveLayer,
                uvTransform: sidecarSettings.UvTransform,
                detailLayer: detailLayer);
        }

        private MaterialLayer? TryLoadDetailLayer(Dictionary<string, string> references, SidecarMaterialSettings settings)
        {
            if (settings.DetailBlend is not { } blend ||
                !references.TryGetValue("Detail", out var detailReference) ||
                IsNullReference(detailReference) ||
                TryLoadTexture(detailReference, hasAlpha: false) is not { } texture)
            {
                return null;
            }

            return new MaterialLayer(texture, blend, settings.DetailUvTransform);
        }

        // Alternate materials listed in the material's sidecar (UE2 state variants), resolved the
        // same way as mesh materials.
        public IReadOnlyList<Material> ResolveVariants(PSKMaterial material, string meshName, string meshPath)
        {
            if (!TryGetLocalSidecar(material, meshPath, ".mat", out var materialPath) &&
                !TryGetSidecar(_materialSidecars, material, out materialPath))
            {
                return [];
            }

            return [.. ReadSidecarSettings(materialPath).Variants
                .Select(name => Resolve(new PSKMaterial(name, name), meshName, meshPath))];
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
            var matchedKey = GetMaterialLookupKeys(material).FirstOrDefault(sidecars.ContainsKey);
            if (matchedKey is not null)
            {
                path = sidecars[matchedKey];
                return true;
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

        // UE2 shaders take glow colour from SelfIllumination and gate it with SelfIlluminationMask's
        // alpha. When both share coordinates the glow is their per-pixel product. When the mask
        // scrolls separately that product can't be baked, so the colour texture's average tint is
        // applied to the mask instead, keeping the masked shapes (and their own scroll) and the
        // overall glow colour. When the colour scrolls under a fixed mask, the product is baked
        // unscrolled and the two are also returned as an emissive layer for exporters that can
        // animate it. A ConstantColor combined into the colour is applied first. A plain or fading
        // colour in place of the texture ("Replace") is also returned as a layer, so the glow keeps
        // that colour (and its pulse) instead of the albedo's.
        private (Texture? Emissive, MaterialEmissiveLayer? Layer) TryBakeMaskedSelfIllumination(
            Dictionary<string, string> references,
            SidecarMaterialSettings settings)
        {
            var replace = settings.SelfIlluminationColor is { Operation: var operation } &&
                operation.Equals("Replace", StringComparison.OrdinalIgnoreCase);
            if (!references.TryGetValue("SelfIlluminationMask", out var maskReference) || IsNullReference(maskReference))
            {
                return (null, null);
            }

            string? colorReference = null;
            if (!replace &&
                (!references.TryGetValue("SelfIllumination", out colorReference) || IsNullReference(colorReference)))
            {
                return (null, null);
            }

            using var color = replace
                ? SolidTexture($"{maskReference}_color", settings.SelfIlluminationColor!.Color)
                : TryLoadTexture(colorReference!, hasAlpha: false);
            using var mask = TryLoadTexture(maskReference, hasAlpha: true);
            if (color is null || mask is null)
            {
                return (null, null);
            }

            var colorPixels = color.GetRgbaPixels();
            if (!replace && settings.SelfIlluminationColor is { } constant)
            {
                CombinerCompositor.ApplyConstantColor(colorPixels, constant.Color, constant.Operation);
            }

            var separatelyScrolled = settings.EmissiveUvScrollRate is not null;
            if (!separatelyScrolled)
            {
                // Bake at the mask's resolution, sampling the colour texture nearest-neighbour when
                // the two differ (e.g. a small glow pattern gated by a large diffuse's alpha).
                var maskPixels = mask.GetRgbaPixels();
                var glow = new byte[maskPixels.Length];
                for (var y = 0; y < mask.Height; y++)
                {
                    var colorY = Math.Min(color.Height - 1, y * color.Height / mask.Height);
                    for (var x = 0; x < mask.Width; x++)
                    {
                        var colorX = Math.Min(color.Width - 1, x * color.Width / mask.Width);
                        var c = ((colorY * color.Width) + colorX) * 4;
                        var m = ((y * mask.Width) + x) * 4;
                        var coverage = maskPixels[m + 3] / 255.0;
                        glow[m] = (byte)Math.Round(colorPixels[c] * coverage);
                        glow[m + 1] = (byte)Math.Round(colorPixels[c + 1] * coverage);
                        glow[m + 2] = (byte)Math.Round(colorPixels[c + 2] * coverage);
                        glow[m + 3] = byte.MaxValue;
                    }
                }

                var baked = _textureFactory.FromRgba($"{mask.Name}_glow", mask.Width, mask.Height, glow, hasAlpha: false);
                var scroll = settings.SelfIlluminationUvScrollRate ?? System.Numerics.Vector2.Zero;
                if (scroll == System.Numerics.Vector2.Zero && !replace)
                {
                    return (baked, null);
                }

                // A fade pulses a white layer between its two colours.
                var pulse = replace && settings.SelfIlluminationFade is { } fade
                    ? new MaterialColorPulse(
                        ToColor(settings.SelfIlluminationColor!.Color),
                        ToColor(fade.Color),
                        fade.Period,
                        fade.Phase)
                    : null;
                var layerColor = pulse is null
                    ? _textureFactory.FromRgba($"{color.Name}_glow", color.Width, color.Height, colorPixels)
                    : SolidTexture($"{color.Name}_glow", [byte.MaxValue, byte.MaxValue, byte.MaxValue]);
                return (baked, new MaterialEmissiveLayer(
                    layerColor,
                    _textureFactory.FromRgba(mask.Name, mask.Width, mask.Height, maskPixels, hasAlpha: true),
                    scroll,
                    pulse));
            }

            var texelCount = Math.Max(1, colorPixels.Length / 4);
            Span<double> tint = stackalloc double[3];
            for (var i = 0; i < colorPixels.Length; i += 4)
            {
                tint[0] += colorPixels[i];
                tint[1] += colorPixels[i + 1];
                tint[2] += colorPixels[i + 2];
            }

            var pixels = mask.GetRgbaPixels();
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var coverage = pixels[i + 3] / 255.0;
                pixels[i] = (byte)Math.Round(tint[0] / texelCount * coverage);
                pixels[i + 1] = (byte)Math.Round(tint[1] / texelCount * coverage);
                pixels[i + 2] = (byte)Math.Round(tint[2] / texelCount * coverage);
                pixels[i + 3] = byte.MaxValue;
            }

            return (_textureFactory.FromRgba($"{mask.Name}_glow", mask.Width, mask.Height, pixels, hasAlpha: false), null);
        }

        // 4x4 rather than 1x1 so block-compressed VTFs keep a whole block.
        private Texture SolidTexture(string name, ReadOnlySpan<byte> rgb)
        {
            var pixels = new byte[4 * 4 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = rgb[0];
                pixels[i + 1] = rgb[1];
                pixels[i + 2] = rgb[2];
                pixels[i + 3] = byte.MaxValue;
            }

            return _textureFactory.FromRgba(name, 4, 4, pixels);
        }

        private static System.Numerics.Vector3 ToColor(byte[] rgb) => new(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f);

        // A Combiner's masked blend recorded as DiffuseOverlay/DiffuseOverlayMask (e.g. a damage
        // decal over the body texture) is baked into the diffuse.
        private Texture? TryBlendDiffuseOverlay(Texture? diffuse, Dictionary<string, string> references)
        {
            if (diffuse is null ||
                !references.TryGetValue("DiffuseOverlay", out var overlayReference) ||
                !references.TryGetValue("DiffuseOverlayMask", out var maskReference))
            {
                return diffuse;
            }

            using var overlay = TryLoadTexture(overlayReference, hasAlpha: false);
            using var mask = TryLoadTexture(maskReference, hasAlpha: true);
            if (overlay is null || mask is null)
            {
                return diffuse;
            }

            using (diffuse)
            {
                return CombinerCompositor.BlendWithMask(diffuse, overlay, mask, _textureFactory);
            }
        }

        // excludedKeyTerms guards substring collisions between channels, e.g. the packed-texture
        // term "orm" also matches "Normal", which would load a normal map as specular.
        private Texture? TryLoadTextureByName(
            Dictionary<string, string> references,
            IReadOnlyCollection<string> keyTerms,
            bool hasAlpha,
            IReadOnlyCollection<string>? excludedKeyTerms = null)
        {
            foreach (var (key, textureReference) in references)
            {
                if (IsNullReference(textureReference) ||
                    !keyTerms.Any(term => key.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (excludedKeyTerms?.Any(term => key.Contains(term, StringComparison.OrdinalIgnoreCase)) ?? false))
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

            foreach (var token in materialContext.Tokens.Where(t => textureName.Contains(t.Value, StringComparison.OrdinalIgnoreCase)))
            {
                score += Math.Min(token.Value.Length, 12) * token.Weight;
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

                foreach (var segment in SplitCamelCaseToken(token).Where(IsUsefulMaterialTextureToken))
                {
                    yield return segment;
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
                // Decode with ImageSharp for the alpha-strip pass (when hasAlpha=false, force
                // every pixel's A to 255 so downstream consumers don't accidentally treat the
                // source's incidental alpha as transparency). Then hand the raw RGBA bytes to
                // the host's texture factory — the plugin doesn't construct Texture instances
                // directly; the host owns the concrete impl.
                using var image = SidecarImageLoader.Load(imagePath, hasAlpha);
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

                var pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
                return _textureFactory.FromRgba(textureName, image.Width, image.Height, pixels, hasAlpha);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                _ = ex;
                return null;
            }
        }

        private Texture? TryCreateColorTexture(
            string materialName,
            IReadOnlyDictionary<string, CueMaterialColor> colors)
        {
            if (!TrySelectBaseColor(colors, out var color))
            {
                return null;
            }

            var textureName = $"{NameHelpers.SanitizeMaterialName(materialName)}_color";
            ReadOnlySpan<byte> pixel =
            [
                ToColorByte(color.R),
                ToColorByte(color.G),
                ToColorByte(color.B),
                byte.MaxValue,
            ];
            return _textureFactory.FromRgba(textureName, 1, 1, pixel);
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
                if (_sidecarSettingKeys.Contains(key))
                {
                    continue;
                }

                var value = NormalizeReference(trimmed[(separator + 1)..]);
                if (value.Length == 0)
                {
                    continue;
                }

                references.TryAdd(key, value);
            }

            return references;
        }

        // Non-texture sidecar entries written by the UE2 material exporter. Read raw, since texture
        // reference values are name-normalized, and kept out of the texture lookups.
        private static readonly HashSet<string> _sidecarSettingKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            UnrealMaterialExporter.UvScrollKey,
            UnrealMaterialExporter.EmissiveUvScrollKey,
            UnrealMaterialExporter.SelfIlluminationUvScrollKey,
            UnrealMaterialExporter.SelfIlluminationColorKey,
            UnrealMaterialExporter.SelfIlluminationColorOperationKey,
            UnrealMaterialExporter.VariantsKey,
            UnrealMaterialExporter.UvTransformKey,
            UnrealMaterialExporter.DetailUvTransformKey,
            UnrealMaterialExporter.DetailBlendKey,
            UnrealMaterialExporter.SelfIlluminationFadeKey,
            UnrealMaterialExporter.BlendKey,
            UnrealMaterialExporter.AlphaRefKey
        };

        private static SidecarMaterialSettings ReadSidecarSettings(string materialPath)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(materialPath))
            {
                var separator = line.IndexOf('=');
                if (separator > 0 && _sidecarSettingKeys.Contains(line[..separator].Trim()))
                {
                    values.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim());
                }
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            System.Numerics.Vector2? ParseScroll(string key)
            {
                var parts = values.GetValueOrDefault(key)?.Split(',', StringSplitOptions.TrimEntries);
                return parts is { Length: 2 } &&
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out var u) &&
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out var v)
                    ? new System.Numerics.Vector2(u, v)
                    : null;
            }

            var blendMode = ParseSidecarBlendMode(values.GetValueOrDefault(UnrealMaterialExporter.BlendKey));

            // UE2 keeps pixels whose alpha exceeds AlphaRef; a zero reference still drops fully
            // transparent texels, so never let the cutoff reach zero.
            var alphaCutoff = int.TryParse(values.GetValueOrDefault(UnrealMaterialExporter.AlphaRefKey), System.Globalization.NumberStyles.Integer, inv, out var alphaRef)
                ? Math.Clamp(alphaRef, 1, 255) / 255f
                : SidecarMaterialSettings.Default.AlphaCutoff;

            var colorParts = values.GetValueOrDefault(UnrealMaterialExporter.SelfIlluminationColorKey)?.Split(',', StringSplitOptions.TrimEntries);
            var colorOperation = values.GetValueOrDefault(UnrealMaterialExporter.SelfIlluminationColorOperationKey);
            byte[] color = new byte[3];
            var selfIlluminationColor = colorParts is { Length: 3 } && colorOperation is not null &&
                colorParts.Select((part, index) => byte.TryParse(part, System.Globalization.NumberStyles.Integer, inv, out color[index])).All(parsed => parsed)
                ? new SidecarConstantColor(color, colorOperation)
                : null;

            var variants = values.GetValueOrDefault(UnrealMaterialExporter.VariantsKey)?
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

            // "R,G,B,period,phase"; UE2's FadePeriod is the time from one colour to the other, so a
            // full cycle takes twice as long.
            var fadeParts = values.GetValueOrDefault(UnrealMaterialExporter.SelfIlluminationFadeKey)?.Split(',', StringSplitOptions.TrimEntries);
            var fadeColor = new byte[3];
            SidecarFade? fade = fadeParts is { Length: 5 } &&
                fadeParts[..3].Select((part, index) => byte.TryParse(part, System.Globalization.NumberStyles.Integer, inv, out fadeColor[index])).All(parsed => parsed) &&
                float.TryParse(fadeParts[3], System.Globalization.NumberStyles.Float, inv, out var fadePeriod) &&
                float.TryParse(fadeParts[4], System.Globalization.NumberStyles.Float, inv, out var fadePhase) &&
                fadePeriod > 0f
                ? new SidecarFade(fadeColor, fadePeriod * 2f, fadePhase)
                : null;

            return new SidecarMaterialSettings(
                ParseScroll(UnrealMaterialExporter.UvScrollKey),
                ParseScroll(UnrealMaterialExporter.EmissiveUvScrollKey),
                blendMode,
                alphaCutoff,
                ParseScroll(UnrealMaterialExporter.SelfIlluminationUvScrollKey),
                selfIlluminationColor,
                variants,
                PSKUvTransformParser.Parse(values.GetValueOrDefault(UnrealMaterialExporter.UvTransformKey)),
                PSKUvTransformParser.Parse(values.GetValueOrDefault(UnrealMaterialExporter.DetailUvTransformKey)),
                PSKUvTransformParser.ParseBlend(values.GetValueOrDefault(UnrealMaterialExporter.DetailBlendKey)),
                fade);
        }

        private sealed record SidecarMaterialSettings(
            System.Numerics.Vector2? UvScrollRate,
            System.Numerics.Vector2? EmissiveUvScrollRate,
            MaterialBlendMode BlendMode,
            float AlphaCutoff,
            System.Numerics.Vector2? SelfIlluminationUvScrollRate,
            SidecarConstantColor? SelfIlluminationColor,
            IReadOnlyList<string> Variants,
            MaterialUvTransform? UvTransform,
            MaterialUvTransform? DetailUvTransform,
            MaterialLayerBlend? DetailBlend,
            SidecarFade? SelfIlluminationFade)
        {
            public static SidecarMaterialSettings Default { get; } = new(null, null, MaterialBlendMode.Unspecified, 0.5f, null, null, [], null, null, null, null);
        }

        private sealed record SidecarConstantColor(byte[] Color, string Operation);

        // Period is a full cycle, in seconds.
        private sealed record SidecarFade(byte[] Color, float Period, float Phase);

        // A cut-out texture has clearly transparent regions; packed data in the alpha channel
        // (roughness, AO, fade masks) almost never reaches exactly zero across a meaningful area.
        private static bool HasCutoutTexels(Texture texture, out byte[] pixels)
        {
            const double minimumClearFraction = 0.01;
            pixels = texture.GetRgbaPixels();
            var texelCount = pixels.Length / 4;
            if (texelCount == 0)
            {
                return false;
            }

            var clearCount = 0;
            for (var index = 3; index < pixels.Length; index += 4)
            {
                if (pixels[index] == 0)
                {
                    clearCount++;
                }
            }

            return clearCount >= texelCount * minimumClearFraction;
        }

        private static MaterialBlendMode ParseSidecarBlendMode(string? value)
        {
            return value?.ToLowerInvariant() switch
            {
                "translucent" => MaterialBlendMode.AlphaBlend,
                "masked" => MaterialBlendMode.AlphaTest,
                "additive" => MaterialBlendMode.Additive,
                _ => MaterialBlendMode.Unspecified
            };
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

            var blendSettings = SidecarMaterialSettings.Default;
            if (document.RootElement.TryGetProperty("BlendMode", out var blendModeEl) &&
                blendModeEl.ValueKind is JsonValueKind.String)
            {
                blendSettings = blendSettings with
                {
                    BlendMode = ParseSidecarBlendMode(blendModeEl.GetString()),
                    AlphaCutoff = document.RootElement.TryGetProperty("AlphaCutoff", out var cutoffEl) &&
                        cutoffEl.ValueKind is JsonValueKind.Number
                            ? Math.Clamp(cutoffEl.GetSingle(), 1 / 255f, 1f)
                            : blendSettings.AlphaCutoff
                };
            }

            return new CueMaterial(references, colors, bakedUv0Scale, materialAlias, blendSettings);
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
            string? MaterialAlias = null,
            SidecarMaterialSettings? BlendSettingsOverride = null)
        {
            public SidecarMaterialSettings BlendSettings => BlendSettingsOverride ?? SidecarMaterialSettings.Default;
        }

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
