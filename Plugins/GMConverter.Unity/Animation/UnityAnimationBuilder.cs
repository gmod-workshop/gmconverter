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
    private const int _leftHandGoalAttribute = 28;
    private const int _rightHandGoalAttribute = 35;
    private const int _firstMuscleAttribute = 42;

    private readonly UnitySkeleton _skeleton;
    private readonly UnityTransformNode[] _bones;
    private readonly Dictionary<uint, UnityTransformNode> _nodesByPathHash = [];
    private readonly UnityHumanoidSolver? _solver;
    private readonly (int Node, UnityTransformNode Bone)[] _humanBones = [];
    private readonly UnityTransformNode? _hipsBone;

    public UnityAnimationBuilder(UnitySkeleton skeleton, UnityTransformNode animatorRoot, UnityAvatar? avatar)
    {
        _skeleton = skeleton;
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
            _humanBones = [.. Enumerable.Range(0, avatar.NodeCount)
                .Where(node => avatar.Axes[node] is not null && avatar.NodePaths[node] is { } path && nodesByPath.TryGetValue(path, out var bone) && skeleton.Indices.ContainsKey(bone))
                .Select(node => (node, nodesByPath[avatar.NodePaths[node]!]))];
            if (_solver.HipsNode >= 0 && avatar.NodePaths[_solver.HipsNode] is { } hipsPath)
            {
                _hipsBone = nodesByPath.GetValueOrDefault(hipsPath);
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
        var solveHumanoid = humanoid.Length > 0 && _solver is not null;
        if (!solveHumanoid && transforms.Length == 0)
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

        IAnimationTrack[] tracks = [.. keyframes.Select((frames, bone) => new BoneTransformTrack(bone, frames))];
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
        var leftHandGoal = Vector3.Zero;
        var rightHandGoal = Vector3.Zero;
        var goalCurves = 0;
        foreach (var binding in humanoid)
        {
            var attribute = (int)binding.Attribute;
            var value = values[binding.FirstCurve];
            if (attribute >= _leftHandGoalAttribute && attribute < _leftHandGoalAttribute + 3)
            {
                leftHandGoal[attribute - _leftHandGoalAttribute] = value;
                goalCurves++;
            }
            else if (attribute >= _rightHandGoalAttribute && attribute < _rightHandGoalAttribute + 3)
            {
                rightHandGoal[attribute - _rightHandGoalAttribute] = value;
                goalCurves++;
            }
            else if (attribute >= _rootPositionAttribute && attribute < _rootRotationAttribute)
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
        var solved = _solver!.Solve(muscles, rootPosition, rotation, goalCurves == 6 ? (leftHandGoal, rightHandGoal) : null);

        // Bones keep their own lengths; only the hips move.
        foreach (var (node, bone) in _humanBones)
        {
            locals[bone] = bone == _hipsBone
                ? solved[node]
                : new UnityXform(bone.LocalPosition, solved[node].Rotation, bone.LocalScale);
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
