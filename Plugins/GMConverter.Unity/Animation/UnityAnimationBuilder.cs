using System.Numerics;
using GMConverter.SDK.Animation;
using GMConverter.SDK.Geometry;
using GMConverter.Unity.Assets;
using GMConverter.Unity.Geometry;
using GMConverter.Unity.Models;
using GMConverter.Unity.Scenes;

namespace GMConverter.Unity.Animation;

/// <summary>
/// Samples Unity animation clips onto a model's skeleton. Transform curves move the bones their
/// paths name; humanoid curves are solved through the animator's avatar, so any humanoid clip
/// plays on any humanoid character.
/// </summary>
internal sealed class UnityAnimationBuilder
{
    private const float _defaultSampleRate = 30f;
    private const int _rootPositionAttribute = 7;
    private const int _rootRotationAttribute = 10;
    private const int _firstMuscleAttribute = 42;

    private readonly UnitySkeleton _skeleton;
    private readonly UnityTransformNode _animatorRoot;
    private readonly UnityTransformNode[] _toggledBones;
    private readonly UnityTransformNode[] _bones;
    private readonly Dictionary<uint, UnityTransformNode> _nodesByPathHash = [];
    private readonly UnityHumanoidSolver? _solver;
    private readonly (int Node, UnityTransformNode Bone, Quaternion Between)[] _humanBones = [];
    private readonly UnityTransformNode? _hipsBone;

    public UnityAnimationBuilder(UnitySkeleton skeleton, UnityTransformNode animatorRoot, UnityAvatar? avatar, IReadOnlySet<UnityTransformNode>? toggledBones = null)
    {
        _skeleton = skeleton;
        _animatorRoot = animatorRoot;
        _toggledBones = [.. (toggledBones ?? new HashSet<UnityTransformNode>())
            .Where(node => skeleton.Indices.ContainsKey(node) && node.Ancestors().Prepend(node).Contains(animatorRoot))];
        _bones = new UnityTransformNode[skeleton.Skeleton.Bones.Count];
        foreach (var (node, index) in skeleton.Indices)
        {
            _bones[index] = node;
        }

        Dictionary<string, UnityTransformNode> nodesByPath = new(StringComparer.Ordinal);
        foreach (var (node, path) in Paths(animatorRoot, string.Empty))
        {
            nodesByPath[path] = node;
            _nodesByPathHash[UnityPathHash.Compute(path)] = node;
        }

        if (avatar is not null)
        {
            _solver = new UnityHumanoidSolver(avatar);
            // Unity binds human bones by name, so a game can put nodes between them (Liar's Bar
            // adds a head pivot above the head); fall back to a uniquely named node.
            var byName = nodesByPath.Values
                .GroupBy(node => node.Name, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            UnityTransformNode? Find(int node) => avatar.NodePaths[node] is not { } path
                ? null
                : nodesByPath.GetValueOrDefault(path) ?? byName.GetValueOrDefault(path[(path.LastIndexOf('/') + 1)..]);

            _humanBones = [.. Enumerable.Range(0, avatar.NodeCount)
                .Where(node => avatar.Axes[node] is not null && Find(node) is { } bone && skeleton.Indices.ContainsKey(bone))
                .Select(node => (node, Find(node)!, Between(avatar.Parents[node] >= 0 ? Find(avatar.Parents[node]) : null, Find(node)!)))];
            if (_solver.HipsNode >= 0)
            {
                _hipsBone = Find(_solver.HipsNode);
            }
        }
    }

    /// <summary>Whether the model can play humanoid clips.</summary>
    public bool IsHumanoid => _solver is not null;

    /// <summary>Every node under <paramref name="root"/> (itself included) by the hash of its path from it.</summary>
    public static Dictionary<uint, UnityTransformNode> NodesByPathHash(UnityTransformNode root)
    {
        Dictionary<uint, UnityTransformNode> nodes = [];
        foreach (var (node, path) in Paths(root, string.Empty))
        {
            nodes[UnityPathHash.Compute(path)] = node;
        }

        return nodes;
    }

    /// <summary>Whether <paramref name="clip"/> holds humanoid (muscle) curves.</summary>
    public static bool HasHumanoidCurves(UnityObject clip)
    {
        return UnityClipBinding.Read(clip["m_ClipBindingConstant"]).Any(binding => binding.IsHumanoid);
    }

    /// <summary>Samples <paramref name="clip"/>, or returns null when nothing in it moves this skeleton.</summary>
    public AnimationClip? Build(UnityObject clip, string name)
    {
        var muscleClip = clip["m_MuscleClip"];
        var curves = UnityClipCurves.Read(muscleClip["m_Clip"]["data"]);
        var bindings = UnityClipBinding.Read(clip["m_ClipBindingConstant"]);
        var humanoid = bindings.Where(binding => binding.IsHumanoid).ToArray();
        var transforms = bindings
            .Where(binding => binding.TransformCurve != UnityTransformCurve.None && _nodesByPathHash.ContainsKey(binding.Path))
            .ToArray();
        var toggles = bindings
            .Where(binding => binding.IsActiveToggle && _nodesByPathHash.ContainsKey(binding.Path))
            .ToDictionary(binding => _nodesByPathHash[binding.Path], binding => binding.FirstCurve);
        var solveHumanoid = humanoid.Length > 0 && _solver is not null;
        if (!solveHumanoid && transforms.Length == 0 && !_toggledBones.Any(bone => Chain(bone).Any(toggles.ContainsKey)))
        {
            return null;
        }

        var start = muscleClip["m_StartTime"].AsFloat;
        var stop = Math.Max(start, muscleClip["m_StopTime"].AsFloat);
        var sampleRate = clip["m_SampleRate"].AsFloat is var rate and > 0f ? rate : _defaultSampleRate;
        var frameCount = Math.Max(1, (int)MathF.Round((stop - start) * sampleRate)) + 1;

        var values = new float[curves.CurveCount];
        var muscles = new float[UnityHumanoidSolver.MuscleCount];
        var keyframes = _bones.Select(_ => new List<TransformKeyframe>(frameCount)).ToArray();
        var visibility = _toggledBones.ToDictionary(bone => bone, _ => new List<VisibilityKeyframe>());
        var locals = new Dictionary<UnityTransformNode, UnityXform>();

        for (var frame = 0; frame < frameCount; frame++)
        {
            var time = Math.Min(start + (frame / sampleRate), stop);
            curves.Sample(time, values);
            locals.Clear();

            if (solveHumanoid)
            {
                ApplyHumanoid(humanoid, values, muscles, locals);
            }

            foreach (var binding in transforms)
            {
                ApplyTransform(binding, values, locals);
            }

            foreach (var (bone, frames) in visibility)
            {
                var visible = Chain(bone).All(node => toggles.TryGetValue(node, out var curve)
                    ? values[curve] > 0.5f
                    : node.GameObject?["m_IsActive"].AsBool != false);
                if (frames.Count == 0 || frames[^1].Visible != visible)
                {
                    frames.Add(new VisibilityKeyframe(frame / sampleRate, visible));
                }
            }

            for (var bone = 0; bone < _bones.Length; bone++)
            {
                var node = _bones[bone];
                var local = locals.TryGetValue(node, out var animated)
                    ? animated
                    : new UnityXform(node.LocalPosition, node.LocalRotation, node.LocalScale);
                keyframes[bone].Add(new TransformKeyframe(
                    frame / sampleRate,
                    new Transform(UnitySpace.Position(local.Translation), UnitySpace.Rotation(local.Rotation), UnitySpace.Scale(local.Scale))));
            }
        }

        IAnimationTrack[] tracks =
        [
            .. keyframes.Select((frames, bone) => new BoneTransformTrack(bone, frames)),
            .. visibility.Select(pair => new BoneVisibilityTrack(_skeleton.Indices[pair.Key], pair.Value)),
        ];
        return new AnimationClip(
            name,
            sampleRate,
            (frameCount - 1) / sampleRate,
            tracks,
            Loops: muscleClip["m_LoopTime"].AsBool);
    }

    private void ApplyHumanoid(UnityClipBinding[] humanoid, float[] values, float[] muscles, Dictionary<UnityTransformNode, UnityXform> locals)
    {
        Array.Clear(muscles);
        var rootPosition = Vector3.Zero;
        var rootRotation = new Vector4(0f, 0f, 0f, 1f);
        foreach (var binding in humanoid)
        {
            var attribute = (int)binding.Attribute;
            var value = values[binding.FirstCurve];
            if (attribute >= _rootPositionAttribute && attribute < _rootRotationAttribute)
            {
                rootPosition[attribute - _rootPositionAttribute] = value;
            }
            else if (attribute >= _rootRotationAttribute && attribute < _rootRotationAttribute + 4)
            {
                rootRotation[attribute - _rootRotationAttribute] = value;
            }
            else if (attribute >= _firstMuscleAttribute && attribute < _firstMuscleAttribute + muscles.Length)
            {
                muscles[attribute - _firstMuscleAttribute] = value;
            }
        }

        var rotation = rootRotation.LengthSquared() > 1e-12f
            ? Quaternion.Normalize(new Quaternion(rootRotation.X, rootRotation.Y, rootRotation.Z, rootRotation.W))
            : Quaternion.Identity;
        var solved = _solver!.Solve(muscles, rootPosition, rotation);

        // Bones keep their own lengths; only the hips move. Nodes the game put between a bone and
        // its human parent keep their own turn, which the bone's rotation then leaves out.
        foreach (var (node, bone, between) in _humanBones)
        {
            locals[bone] = bone == _hipsBone
                ? solved[node]
                : new UnityXform(bone.LocalPosition, Quaternion.Normalize(Quaternion.Conjugate(between) * solved[node].Rotation), bone.LocalScale);
        }
    }

    private void ApplyTransform(UnityClipBinding binding, float[] values, Dictionary<UnityTransformNode, UnityXform> locals)
    {
        var node = _nodesByPathHash[binding.Path];
        var local = locals.TryGetValue(node, out var current)
            ? current
            : new UnityXform(node.LocalPosition, node.LocalRotation, node.LocalScale);
        var first = binding.FirstCurve;
        var vector = new Vector3(values[first], values[first + 1], values[first + 2]);

        locals[node] = binding.TransformCurve switch
        {
            UnityTransformCurve.Position => local with { Translation = vector },
            UnityTransformCurve.Scale => local with { Scale = vector },
            UnityTransformCurve.Rotation => local with
            {
                Rotation = Quaternion.Normalize(new Quaternion(vector, values[first + 3])),
            },
            UnityTransformCurve.Euler => local with { Rotation = Euler(vector) },
            _ => local,
        };
    }

    // The combined saved rotation of the nodes strictly between a human bone and its human parent.
    private static Quaternion Between(UnityTransformNode? humanParent, UnityTransformNode bone)
    {
        var between = Quaternion.Identity;
        for (var node = bone.Parent; node is not null && node != humanParent; node = node.Parent)
        {
            between = node.LocalRotation * between;
        }

        return humanParent is not null && bone.Ancestors().Contains(humanParent) ? between : Quaternion.Identity;
    }

    // A node and the nodes above it up to the animated root: all must be switched on for it to show.
    private IEnumerable<UnityTransformNode> Chain(UnityTransformNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            yield return current;
            if (current == _animatorRoot)
            {
                yield break;
            }
        }
    }

    // Unity's Quaternion.Euler: degrees, turning about Z, then X, then Y.
    private static Quaternion Euler(Vector3 degrees)
    {
        var radians = degrees * (MathF.PI / 180f);
        return Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians.Y) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, radians.X) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians.Z));
    }

    private static IEnumerable<(UnityTransformNode Node, string Path)> Paths(UnityTransformNode node, string path)
    {
        yield return (node, path);
        foreach (var child in node.Children)
        {
            foreach (var descendant in Paths(child, path.Length == 0 ? child.Name : $"{path}/{child.Name}"))
            {
                yield return descendant;
            }
        }
    }
}
