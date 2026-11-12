using System.Numerics;
using GMConverter.SDK.Geometry;

namespace GMConverter.SDK.Animation;

/// <summary>
/// Moves a first-person model's arm animations onto another arm skeleton, matching bones by
/// <see cref="HumanoidBone"/> role. Built for view models whose arms another model supplies (Garry's
/// Mod's player hands), so the result keeps only what the arms hold.
/// <para>
/// The source is first scaled about the camera (the model's origin) so its hands are the target's
/// size (by arm length when either lacks fingers); scaling about the eye leaves the picture
/// unchanged, so the target's hands are drawn as big, and where, the source's were. The target's
/// arms then reach those hands from the source's shoulders with their own lengths, the elbows bending
/// in the source arm's plane. Each target bone points the way its source bone does, judged from joint
/// positions rather than bone axes (bones run toward their child, elbows bend in the arm's plane,
/// hands lie across their knuckles), so skeletons with different bone axes and bind poses line up.
/// Held items stay where they were relative to their hand.
/// </para>
/// </summary>
public static class HumanoidRetargeter
{
    private static readonly string[] _knuckleFingers = ["Index1", "Middle1", "Ring1", "Pinky1"];

    private const float _degenerate = 1e-8f;
    private static readonly string[] _sides = ["Left", "Right"];
    private static readonly string[] _knuckles = ["Middle1", "Index1"];

    private static readonly HashSet<HumanoidBone> _anchors =
    [
        HumanoidBone.Chest,
        HumanoidBone.LeftUpperArm,
        HumanoidBone.RightUpperArm,
        HumanoidBone.LeftForearm,
        HumanoidBone.RightForearm,
        HumanoidBone.LeftHand,
        HumanoidBone.RightHand,
    ];

    private static readonly HumanoidBone[][] _fingers =
    [
        [HumanoidBone.LeftThumb1, HumanoidBone.LeftThumb2, HumanoidBone.LeftThumb3],
        [HumanoidBone.LeftIndex1, HumanoidBone.LeftIndex2, HumanoidBone.LeftIndex3],
        [HumanoidBone.LeftMiddle1, HumanoidBone.LeftMiddle2, HumanoidBone.LeftMiddle3],
        [HumanoidBone.LeftRing1, HumanoidBone.LeftRing2, HumanoidBone.LeftRing3],
        [HumanoidBone.LeftPinky1, HumanoidBone.LeftPinky2, HumanoidBone.LeftPinky3],
        [HumanoidBone.RightThumb1, HumanoidBone.RightThumb2, HumanoidBone.RightThumb3],
        [HumanoidBone.RightIndex1, HumanoidBone.RightIndex2, HumanoidBone.RightIndex3],
        [HumanoidBone.RightMiddle1, HumanoidBone.RightMiddle2, HumanoidBone.RightMiddle3],
        [HumanoidBone.RightRing1, HumanoidBone.RightRing2, HumanoidBone.RightRing3],
        [HumanoidBone.RightPinky1, HumanoidBone.RightPinky2, HumanoidBone.RightPinky3],
    ];

    /// <summary>
    /// The model rebuilt on <paramref name="target"/>'s arms: its skeleton is the target's (bones
    /// marked <see cref="Bone.Shared"/>) plus the bones of anything held, its meshes are what is held,
    /// and its clips move the target's arms like the source's.
    /// </summary>
    public static Model RetargetArms(Model source, Skeleton target)
    {
        var sourceSkeleton = source.Skeleton ?? throw new InvalidOperationException($"{source.Name} has no skeleton to retarget.");
        var sourceRoles = RoleIndex(sourceSkeleton);
        var targetRoles = RoleIndex(target);
        foreach (var required in new[] { HumanoidBone.Chest, HumanoidBone.LeftUpperArm, HumanoidBone.LeftForearm, HumanoidBone.LeftHand, HumanoidBone.RightUpperArm, HumanoidBone.RightForearm, HumanoidBone.RightHand })
        {
            if (!sourceRoles.ContainsKey(required) || !targetRoles.ContainsKey(required))
            {
                throw new InvalidOperationException($"Retargeting {source.Name} needs a {required} bone on both skeletons.");
            }
        }

        var sourceBind = World(sourceSkeleton, index => sourceSkeleton.Bones[index].LocalBindPose);
        var targetBind = World(target, index => target.Bones[index].LocalBindPose);
        var scale = HandScale(sourceBind, sourceRoles, targetBind, targetRoles) ?? ArmLength(targetBind, targetRoles) / ArmLength(sourceBind, sourceRoles);
        var scaledSourceBind = sourceBind.Select(matrix => Scaled(matrix, scale)).ToArray();

        // Held items: the bones under each held-item bone, re-hung under the target's hand.
        List<int> keptSource = [];
        Dictionary<int, HumanoidBone> heldSide = [];
        foreach (var (held, hand) in new[] { (HumanoidBone.LeftHeldItem, HumanoidBone.LeftHand), (HumanoidBone.RightHeldItem, HumanoidBone.RightHand) })
        {
            if (sourceRoles.TryGetValue(held, out var heldIndex))
            {
                foreach (var index in Subtree(sourceSkeleton, heldIndex))
                {
                    keptSource.Add(index);
                    heldSide[index] = hand;
                }
            }
        }

        // Held items are fitted to the target's hand by the ratio of palm lengths (offset and
        // geometry); with the scene scaled by hand size this is one, unless the hands differ in shape.
        var handScale = new Dictionary<HumanoidBone, float>
        {
            [HumanoidBone.LeftHand] = PalmLength(targetBind, targetRoles, "Left") / PalmLength(scaledSourceBind, sourceRoles, "Left"),
            [HumanoidBone.RightHand] = PalmLength(targetBind, targetRoles, "Right") / PalmLength(scaledSourceBind, sourceRoles, "Right"),
        };
        var heldScale = heldSide.ToDictionary(pair => pair.Key, pair => float.IsFinite(handScale[pair.Value]) && handScale[pair.Value] > 0 ? handScale[pair.Value] : 1f);

        var sourceToOutput = keptSource.Select((index, order) => (index, order)).ToDictionary(pair => pair.index, pair => target.Bones.Count + pair.order);
        var targetRestFrames = Frames(index => Position(targetBind, targetRoles, index));
        var sourceBindFrames = Frames(index => Position(scaledSourceBind, sourceRoles, index));

        // Bind pose: the target's rest, with held items kept where they sit relative to their hand.
        var outputBindWorld = new Matrix4x4[target.Bones.Count + keptSource.Count];
        Array.Copy(targetBind, outputBindWorld, targetBind.Length);
        PlaceHeld(keptSource, heldSide, heldScale, scaledSourceBind, sourceBindFrames, sourceRoles, outputBindWorld, targetRestFrames, targetRoles);

        List<Bone> bones = [.. target.Bones.Select(bone => bone with { Shared = true })];
        foreach (var index in keptSource)
        {
            var sourceBone = sourceSkeleton.Bones[index];
            var parent = sourceToOutput.TryGetValue(sourceBone.ParentIndex, out var keptParent) ? keptParent : targetRoles[heldSide[index]];
            var output = sourceToOutput[index];
            bones.Add(sourceBone with
            {
                Index = output,
                ParentIndex = parent,
                LocalBindPose = ToTransform(outputBindWorld[output] * Inverse(outputBindWorld[parent])),
            });
        }

        var skeleton = new Skeleton(bones);
        var meshes = KeepHeldGeometry(source, keptSource, sourceToOutput, scale, heldScale, scaledSourceBind, outputBindWorld);
        var usedMaterials = meshes.SelectMany(mesh => mesh.Submeshes).Select(submesh => submesh.MaterialName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return source with
        {
            Skeleton = skeleton,
            // IK chains name bones by index in the old skeleton, so they don't carry over.
            IkChains = null,
            Meshes = meshes,
            Materials = [.. source.Materials.Where(material => usedMaterials.Contains(material.Name))],
            Animations = source.Animations?.Select(clip => RetargetClip(clip, sourceSkeleton, sourceRoles, target, targetRoles, scale, keptSource, sourceToOutput, heldSide, heldScale, skeleton)).ToArray(),
            Attachments = source.Attachments?
                .Where(attachment => attachment.BoneIndex is null || sourceToOutput.ContainsKey(attachment.BoneIndex.Value))
                .Select(attachment => attachment with
                {
                    BoneIndex = attachment.BoneIndex is { } bone ? sourceToOutput[bone] : null,
                    LocalTransform = attachment.LocalTransform with
                    {
                        Translation = attachment.LocalTransform.Translation * scale * (attachment.BoneIndex is { } held ? heldScale[held] : 1f),
                    },
                })
                .ToArray(),
            Physics = null,
            BlendSpaces = null,
        };
    }

    /// <summary>
    /// What one hand holds, as a model of its own hanging from that hand's bone: the world model a
    /// game attaches to a character's hand. Takes a model from <see cref="RetargetArms"/>, so the item
    /// sits in the target skeleton's hand; the hand bone is kept (<see cref="Bone.Shared"/>) for the
    /// game to match against the character's, and the arms and their animations are dropped.
    /// </summary>
    /// <param name="retargeted">A model from <see cref="RetargetArms"/>.</param>
    /// <param name="hand">The hand whose item to keep.</param>
    /// <param name="handle">
    /// Where the game's characters close that hand around a handle, in the hand bone's space: the item
    /// is moved, keeping its angle, so that its long axis passes there where it passed nearest the
    /// knuckles. Null keeps the grip the animations give it.
    /// </param>
    public static Model ExtractHeld(Model retargeted, HumanoidBone hand, Vector3? handle = null)
    {
        var skeleton = retargeted.Skeleton ?? throw new InvalidOperationException($"{retargeted.Name} has no skeleton.");
        var handBone = skeleton.Bones.FirstOrDefault(bone => bone.Humanoid == hand)
            ?? throw new InvalidOperationException($"{retargeted.Name} has no {hand} bone.");
        var bind = World(skeleton, index => skeleton.Bones[index].LocalBindPose);
        var held = Subtree(skeleton, handBone.Index).Skip(1).Where(index => !skeleton.Bones[index].Shared).ToArray();

        // The item sits in the hand as the idle animation holds it; a bind pose need not have it
        // in the grip at all. The hand keeps its bind pose and the item moves with it.
        var placed = (Matrix4x4[])bind.Clone();
        if (GripClip(retargeted.Animations) is { } gripClip)
        {
            var tracks = gripClip.Tracks.OfType<BoneTransformTrack>().ToDictionary(track => track.BoneIndex);
            var pose = World(skeleton, index => tracks.TryGetValue(index, out var track) ? Sample(track, 0f) : skeleton.Bones[index].LocalBindPose);
            var handPose = Inverse(pose[handBone.Index]);
            foreach (var index in held)
            {
                placed[index] = pose[index] * handPose * bind[handBone.Index];
            }
        }

        var skinning = held.ToDictionary(index => index, index => Inverse(bind[index]) * placed[index]);
        if (handle is { } target && GripAxis(retargeted, skeleton, bind, hand, held, skinning, bind[handBone.Index]) is { } grip)
        {
            var shift = Vector3.TransformNormal(target - grip, bind[handBone.Index]);
            foreach (var index in held)
            {
                placed[index] *= Matrix4x4.CreateTranslation(shift);
                skinning[index] = Inverse(bind[index]) * placed[index];
            }
        }

        // The hand, then everything under it that isn't part of the arm (the held item's bones).
        List<Bone> kept = [handBone with { ParentIndex = -1, LocalBindPose = ToTransform(bind[handBone.Index]), Shared = true }];
        kept.AddRange(held.Select(index => skeleton.Bones[index] with
        {
            LocalBindPose = ToTransform(placed[index] * Inverse(placed[skeleton.Bones[index].ParentIndex])),
        }));
        var remap = kept.Select((bone, order) => (bone.Index, order)).ToDictionary(pair => pair.Index, pair => pair.order);
        var bones = kept.Select(bone => bone with
        {
            Index = remap[bone.Index],
            ParentIndex = bone.ParentIndex >= 0 && remap.TryGetValue(bone.ParentIndex, out var parent) ? parent : -1,
        }).ToList();
        bones[0] = bones[0] with { ParentIndex = -1 };

        var meshes = retargeted.Meshes
            .Select(mesh => mesh with
            {
                Vertices = [.. mesh.Vertices.Select(vertex => Skin(vertex, skinning) with
                {
                    BoneWeights = vertex.BoneWeights?.Select(weight => weight with { BoneIndex = remap.TryGetValue(weight.BoneIndex, out var index) ? index : 0 }).ToArray(),
                })],
            })
            .ToList();
        return retargeted with
        {
            Skeleton = new Skeleton(bones),
            // IK chains name bones by index in the old skeleton, so they don't carry over.
            IkChains = null,
            Meshes = meshes,
            Animations = null,
            Attachments = retargeted.Attachments?
                .Where(attachment => attachment.BoneIndex is { } bone && remap.ContainsKey(bone))
                .Select(attachment => attachment with { BoneIndex = remap[attachment.BoneIndex!.Value] })
                .ToArray(),
            BlendSpaces = null,
            Forward = null,
            FirstPerson = false,
        };
    }

    // Where the held item's long axis passes nearest the hand's knuckles, in the hand bone's space:
    // the point the fingers close around. Null without knuckle bones or item vertices.
    private static Vector3? GripAxis(Model model, Skeleton skeleton, Matrix4x4[] bind, HumanoidBone hand, int[] held, Dictionary<int, Matrix4x4> skinning, Matrix4x4 handBind)
    {
        var side = hand.ToString()[..^"Hand".Length];
        var toHand = Inverse(handBind);
        var knuckles = _knuckleFingers
            .Select(finger => Enum.TryParse<HumanoidBone>(side + finger, out var role) ? skeleton.Bones.FirstOrDefault(bone => bone.Humanoid == role) : null)
            .OfType<Bone>()
            .Select(bone => Vector3.Transform(bind[bone.Index].Translation, toHand))
            .ToArray();
        var heldSet = held.ToHashSet();
        var points = model.Meshes
            .SelectMany(mesh => mesh.Vertices)
            .Where(vertex => vertex.BoneWeights?.Any(weight => heldSet.Contains(weight.BoneIndex)) == true)
            .Select(vertex => Vector3.Transform(Skin(vertex, skinning).Position, toHand))
            .ToArray();
        if (knuckles.Length == 0 || points.Length < 2)
        {
            return null;
        }

        var knuckle = knuckles.Aggregate(Vector3.Add) / knuckles.Length;
        var centre = points.Aggregate(Vector3.Add) / points.Length;
        var axis = Vector3.Normalize(points[^1] - points[0] + new Vector3(1e-4f, 2e-4f, 3e-4f));
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var next = Vector3.Zero;
            foreach (var point in points)
            {
                next += (point - centre) * Vector3.Dot(point - centre, axis);
            }

            if (next.LengthSquared() == 0f)
            {
                return null;
            }

            axis = Vector3.Normalize(next);
        }

        return centre + (axis * Vector3.Dot(knuckle - centre, axis));
    }

    // The clip whose pose shows how the item is held: idle, else the first clip.
    private static AnimationClip? GripClip(IReadOnlyList<AnimationClip>? clips) =>
        clips?.FirstOrDefault(clip => clip.Role?.Action == AnimationAction.Idle) ?? (clips is { Count: > 0 } ? clips[0] : null);

    // A vertex moved by its bones' skinning matrices, blended by weight; bones without one leave it be.
    private static Vertex Skin(Vertex vertex, Dictionary<int, Matrix4x4> skinning)
    {
        if (vertex.BoneWeights is not { Count: > 0 } weights || !weights.Any(weight => skinning.ContainsKey(weight.BoneIndex)))
        {
            return vertex;
        }

        var position = Vector3.Zero;
        var normal = Vector3.Zero;
        var total = 0f;
        foreach (var weight in weights)
        {
            var matrix = skinning.TryGetValue(weight.BoneIndex, out var skin) ? skin : Matrix4x4.Identity;
            position += Vector3.Transform(vertex.Position, matrix) * weight.Weight;
            normal += Vector3.TransformNormal(vertex.Normal, matrix) * weight.Weight;
            total += weight.Weight;
        }

        return total <= 0f ? vertex : vertex with { Position = position / total, Normal = Vector3.Normalize(normal) };
    }

    private static AnimationClip RetargetClip(
        AnimationClip clip,
        Skeleton sourceSkeleton,
        Dictionary<HumanoidBone, int> sourceRoles,
        Skeleton target,
        Dictionary<HumanoidBone, int> targetRoles,
        float scale,
        List<int> keptSource,
        Dictionary<int, int> sourceToOutput,
        Dictionary<int, HumanoidBone> heldSide,
        Dictionary<int, float> heldScale,
        Skeleton output)
    {
        var tracks = clip.Tracks.OfType<BoneTransformTrack>().ToDictionary(track => track.BoneIndex);
        var times = tracks.Values.OrderByDescending(track => track.Keyframes.Count).FirstOrDefault()?.Keyframes.Select(key => key.TimeSeconds).ToArray() ?? [0f];
        var targetBind = World(target, index => target.Bones[index].LocalBindPose);
        var targetRestFrames = Frames(index => Position(targetBind, targetRoles, index));
        var keys = output.Bones.Select(_ => new List<TransformKeyframe>(times.Length)).ToArray();

        foreach (var time in times)
        {
            var sourceWorld = World(sourceSkeleton, index => tracks.TryGetValue(index, out var track) ? Sample(track, time) : sourceSkeleton.Bones[index].LocalBindPose)
                .Select(matrix => Scaled(matrix, scale))
                .ToArray();
            // The target's arms reach the source's hands from the source's shoulders with their own
            // lengths, the elbow bending in the source arm's plane.
            var posed = ReachHands(sourceWorld, sourceRoles, targetBind, targetRoles);
            Vector3? Posed(HumanoidBone role) => posed.TryGetValue(role, out var position) ? position : Position(sourceWorld, sourceRoles, role);
            var sourceFrames = Frames(Posed);
            var world = new Matrix4x4[output.Bones.Count];

            // Target arms: each bone's rest orientation, turned the way its role's frame has turned.
            for (var index = 0; index < target.Bones.Count; index++)
            {
                var bone = target.Bones[index];
                var parentWorld = bone.ParentIndex >= 0 ? world[bone.ParentIndex] : Matrix4x4.Identity;
                var local = Local(bone.LocalBindPose);
                // The torso and shoulders sit where the source's do, so the arms hang from the same
                // place on screen; from the shoulder out, the target's own bone lengths take over.
                var position = bone.Humanoid is { } anchorRole && _anchors.Contains(anchorRole) && Posed(anchorRole) is { } anchored
                    ? anchored
                    : bone.ParentIndex >= 0 ? Vector3.Transform(bone.LocalBindPose.Translation, parentWorld) : local.Translation;
                if (bone.Humanoid is { } role && sourceFrames(role) is { } sourceFrame && targetRestFrames(role) is { } restFrame)
                {
                    var rotation = Rotation(targetBind[index]) * Inverse(restFrame) * sourceFrame;

                    // A clavicle sits back from the source's shoulder along its own direction, at its
                    // own length, so the shoulder lands in place without stretching the clavicle.
                    var shoulderRole = role switch
                    {
                        HumanoidBone.LeftClavicle => HumanoidBone.LeftUpperArm,
                        HumanoidBone.RightClavicle => HumanoidBone.RightUpperArm,
                        _ => (HumanoidBone?)null,
                    };
                    if (shoulderRole is { } shoulder && sourceRoles.TryGetValue(shoulder, out var sourceShoulder) &&
                        target.Bones.FirstOrDefault(child => child.ParentIndex == index && child.Humanoid == shoulder) is { } upperArm)
                    {
                        position = sourceWorld[sourceShoulder].Translation - Vector3.TransformNormal(upperArm.LocalBindPose.Translation, rotation);
                    }

                    world[index] = rotation with { M41 = position.X, M42 = position.Y, M43 = position.Z };
                }
                else
                {
                    world[index] = local * parentWorld;
                }
            }

            PlaceHeld(keptSource, heldSide, heldScale, sourceWorld, sourceFrames, sourceRoles, world, Frames(index => Position(world, targetRoles, index)), targetRoles);
            for (var index = 0; index < output.Bones.Count; index++)
            {
                var parent = output.Bones[index].ParentIndex;
                keys[index].Add(new TransformKeyframe(time, ToTransform(parent >= 0 ? world[index] * Inverse(world[parent]) : world[index])));
            }
        }

        return clip with { Tracks = [.. keys.Select((list, index) => (IAnimationTrack)new BoneTransformTrack(index, list))] };
    }

    // Held bones (and everything under them) keep their place relative to their hand's frame, so
    // they move rigidly with the target hand. Kept bones fill the end of the output skeleton.
    private static void PlaceHeld(
        List<int> keptSource,
        Dictionary<int, HumanoidBone> heldSide,
        Dictionary<int, float> heldScale,
        Matrix4x4[] sourceWorld,
        Func<HumanoidBone, Matrix4x4?> sourceFrames,
        Dictionary<HumanoidBone, int> sourceRoles,
        Matrix4x4[] outputWorld,
        Func<HumanoidBone, Matrix4x4?> targetFrames,
        Dictionary<HumanoidBone, int> targetRoles)
    {
        for (var order = 0; order < keptSource.Count; order++)
        {
            var index = keptSource[order];
            var hand = heldSide[index];
            if (sourceFrames(hand) is not { } sourceHand || targetFrames(hand) is not { } targetHand)
            {
                continue;
            }

            var sourceHandFrame = sourceHand with { M41 = sourceWorld[sourceRoles[hand]].M41, M42 = sourceWorld[sourceRoles[hand]].M42, M43 = sourceWorld[sourceRoles[hand]].M43 };
            var targetHandPosition = outputWorld[targetRoles[hand]].Translation;
            var targetHandFrame = targetHand with { M41 = targetHandPosition.X, M42 = targetHandPosition.Y, M43 = targetHandPosition.Z };
            var relative = sourceWorld[index] * Inverse(sourceHandFrame);
            relative.Translation *= heldScale[index];
            outputWorld[outputWorld.Length - keptSource.Count + order] = relative * targetHandFrame;
        }
    }

    private static List<Mesh> KeepHeldGeometry(
        Model source,
        List<int> keptSource,
        Dictionary<int, int> sourceToOutput,
        float scale,
        Dictionary<int, float> heldScale,
        Matrix4x4[] scaledSourceBind,
        Matrix4x4[] outputBindWorld)
    {
        var kept = keptSource.ToHashSet();
        List<Mesh> meshes = [];
        foreach (var mesh in source.Meshes)
        {
            Dictionary<int, int> remap = [];
            List<Vertex> vertices = [];
            List<Submesh> submeshes = [];
            int Map(int index)
            {
                if (remap.TryGetValue(index, out var mapped))
                {
                    return mapped;
                }

                var vertex = mesh.Vertices[index];
                var position = Vector3.Zero;
                var normal = Vector3.Zero;
                foreach (var weight in vertex.BoneWeights!)
                {
                    var move = Inverse(scaledSourceBind[weight.BoneIndex]) * outputBindWorld[sourceToOutput[weight.BoneIndex]];
                    var boneLocal = Vector3.Transform(vertex.Position * scale, Inverse(scaledSourceBind[weight.BoneIndex])) * heldScale[weight.BoneIndex];
                    position += Vector3.Transform(boneLocal, outputBindWorld[sourceToOutput[weight.BoneIndex]]) * weight.Weight;
                    normal += Vector3.TransformNormal(vertex.Normal, move) * weight.Weight;
                }

                mapped = vertices.Count;
                remap[index] = mapped;
                vertices.Add(vertex with
                {
                    Position = position,
                    Normal = normal.LengthSquared() > _degenerate ? Vector3.Normalize(normal) : vertex.Normal,
                    BoneWeights = [.. vertex.BoneWeights!.Select(weight => weight with { BoneIndex = sourceToOutput[weight.BoneIndex] })],
                });
                return mapped;
            }

            bool IsHeld(int index) => mesh.Vertices[index].BoneWeights is { Count: > 0 } weights && weights.All(weight => kept.Contains(weight.BoneIndex));
            foreach (var submesh in mesh.Submeshes)
            {
                var triangles = submesh.Triangles
                    .Where(triangle => IsHeld(triangle.A) && IsHeld(triangle.B) && IsHeld(triangle.C))
                    .Select(triangle => new Triangle(Map(triangle.A), Map(triangle.B), Map(triangle.C)))
                    .ToList();
                if (triangles.Count > 0)
                {
                    submeshes.Add(submesh with { Triangles = triangles });
                }
            }

            if (submeshes.Count > 0)
            {
                meshes.Add(mesh with { Vertices = vertices, Submeshes = submeshes });
            }
        }

        return meshes;
    }

    // Each role's frame: X along the bone toward the next joint, Y across it (the elbow's bend axis
    // for the arm, the knuckle line for the hand and fingers), from joint positions only.
    private static Func<HumanoidBone, Matrix4x4?> Frames(Func<HumanoidBone, Vector3?> position)
    {
        Vector3? Between(HumanoidBone from, HumanoidBone to) => position(from) is { } a && position(to) is { } b ? b - a : null;
        Vector3? Mid(HumanoidBone a, HumanoidBone b) => position(a) is { } left && position(b) is { } right ? (left + right) / 2 : null;

        Vector3? ChestUp() => Mid(HumanoidBone.LeftUpperArm, HumanoidBone.RightUpperArm) is { } shoulders && position(HumanoidBone.Chest) is { } chest ? shoulders - chest : null;
        Vector3? Across() => Between(HumanoidBone.RightUpperArm, HumanoidBone.LeftUpperArm);
        bool IsLeft(HumanoidBone role) => role.ToString().StartsWith("Left", StringComparison.Ordinal);
        HumanoidBone Side(HumanoidBone role, string part) => Enum.Parse<HumanoidBone>((IsLeft(role) ? "Left" : "Right") + part);

        // The elbow's bend axis; null for a straight arm, which has no bend plane.
        Vector3? Bend(HumanoidBone role)
        {
            var upper = Between(Side(role, "UpperArm"), Side(role, "Forearm"));
            var fore = Between(Side(role, "Forearm"), Side(role, "Hand"));
            return upper is { } u && fore is { } f && Vector3.Cross(Vector3.Normalize(u), Vector3.Normalize(f)).LengthSquared() > 1e-4f ? Vector3.Cross(u, f) : null;
        }

        Vector3? Knuckles(HumanoidBone role) =>
            Between(Side(role, "Pinky1"), Side(role, "Index1")) ?? Between(Side(role, "Ring1"), Side(role, "Index1")) ?? Bend(role);

        Vector3? HandPrimary(HumanoidBone role) =>
            Between(Side(role, "Hand"), Side(role, "Middle1")) ?? Between(Side(role, "Hand"), Side(role, "Index1")) ?? Between(Side(role, "Forearm"), Side(role, "Hand"));

        return role =>
        {
            var name = role.ToString();
            // Each role's across-axis, with stand-ins for when it lines up with the bone (a straight arm).
            (Vector3? Primary, Vector3?[] Secondaries) axes = role switch
            {
                HumanoidBone.Chest => (ChestUp(), [Across()]),
                HumanoidBone.LeftClavicle or HumanoidBone.RightClavicle => (Between(role, Side(role, "UpperArm")), [ChestUp(), Across()]),
                HumanoidBone.LeftUpperArm or HumanoidBone.RightUpperArm => (Between(role, Side(role, "Forearm")), [Bend(role), ChestUp(), Across()]),
                HumanoidBone.LeftForearm or HumanoidBone.RightForearm => (Between(role, Side(role, "Hand")), [Bend(role), ChestUp(), Across()]),
                HumanoidBone.LeftHand or HumanoidBone.RightHand => (HandPrimary(role), [Knuckles(role), ChestUp(), Across()]),
                _ when FingerChain(role) is { } chain => (FingerDirection(chain, role, Between), name.Contains("Thumb", StringComparison.Ordinal)
                    ? [HandPrimary(role) is { } along && Knuckles(role) is { } across ? Vector3.Cross(along, across) : null, ChestUp()]
                    : [Knuckles(role), ChestUp()]),
                _ => (null, []),
            };
            return axes.Primary is { } primary
                ? axes.Secondaries.OfType<Vector3>().Select(secondary => Basis(primary, secondary)).FirstOrDefault(basis => basis is not null)
                : null;
        };
    }

    private static HumanoidBone[]? FingerChain(HumanoidBone role) => _fingers.FirstOrDefault(chain => chain.Contains(role));

    private static Vector3? FingerDirection(HumanoidBone[] chain, HumanoidBone role, Func<HumanoidBone, HumanoidBone, Vector3?> between)
    {
        var joint = Array.IndexOf(chain, role);
        return joint < chain.Length - 1 ? between(role, chain[joint + 1]) : between(chain[joint - 1], role);
    }

    private static Matrix4x4? Basis(Vector3 primary, Vector3 secondary)
    {
        if (primary.LengthSquared() < _degenerate)
        {
            return null;
        }

        var x = Vector3.Normalize(primary);
        var y = secondary - x * Vector3.Dot(secondary, x);
        if (secondary.LengthSquared() < _degenerate || y.LengthSquared() < 1e-4f * secondary.LengthSquared())
        {
            return null;
        }

        y = Vector3.Normalize(y);
        var z = Vector3.Cross(x, y);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    private static Vector3? Position(Matrix4x4[] world, Dictionary<HumanoidBone, int> roles, HumanoidBone role) =>
        roles.TryGetValue(role, out var index) ? world[index].Translation : null;

    private static float ArmLength(Matrix4x4[] world, Dictionary<HumanoidBone, int> roles)
    {
        float Side(HumanoidBone upper, HumanoidBone fore, HumanoidBone hand) =>
            Vector3.Distance(world[roles[upper]].Translation, world[roles[fore]].Translation) +
            Vector3.Distance(world[roles[fore]].Translation, world[roles[hand]].Translation);
        return (Side(HumanoidBone.LeftUpperArm, HumanoidBone.LeftForearm, HumanoidBone.LeftHand) +
            Side(HumanoidBone.RightUpperArm, HumanoidBone.RightForearm, HumanoidBone.RightHand)) / 2;
    }

    // How much bigger the target's hands are than the source's (by palm length), so the scaled
    // source hands come out the target's size; null when either skeleton lacks knuckles.
    private static float? HandScale(Matrix4x4[] sourceBind, Dictionary<HumanoidBone, int> sourceRoles, Matrix4x4[] targetBind, Dictionary<HumanoidBone, int> targetRoles)
    {
        var ratios = _sides
            .Select(side => PalmLength(targetBind, targetRoles, side) / PalmLength(sourceBind, sourceRoles, side))
            .Where(ratio => float.IsFinite(ratio) && ratio > 0)
            .ToArray();
        return ratios.Length == 0 ? null : ratios.Average();
    }

    // Two-bone reach: each target arm hangs from the source's shoulder and puts its hand on the
    // source's hand (or as close as its length allows), the elbow on the side the source's bends to.
    private static Dictionary<HumanoidBone, Vector3> ReachHands(
        Matrix4x4[] sourceWorld,
        Dictionary<HumanoidBone, int> sourceRoles,
        Matrix4x4[] targetBind,
        Dictionary<HumanoidBone, int> targetRoles)
    {
        Dictionary<HumanoidBone, Vector3> posed = [];
        foreach (var side in _sides)
        {
            var (upper, fore, hand) = (Enum.Parse<HumanoidBone>(side + "UpperArm"), Enum.Parse<HumanoidBone>(side + "Forearm"), Enum.Parse<HumanoidBone>(side + "Hand"));
            var shoulder = sourceWorld[sourceRoles[upper]].Translation;
            var sourceElbow = sourceWorld[sourceRoles[fore]].Translation;
            var reach = sourceWorld[sourceRoles[hand]].Translation - shoulder;
            var upperLength = Vector3.Distance(targetBind[targetRoles[upper]].Translation, targetBind[targetRoles[fore]].Translation);
            var foreLength = Vector3.Distance(targetBind[targetRoles[fore]].Translation, targetBind[targetRoles[hand]].Translation);
            var distance = Math.Clamp(reach.Length(), MathF.Abs(upperLength - foreLength) + 1e-4f, (upperLength + foreLength) * 0.9999f);
            if (reach.LengthSquared() < _degenerate)
            {
                continue;
            }

            var along = Vector3.Normalize(reach);
            var bendSide = sourceElbow - shoulder - along * Vector3.Dot(sourceElbow - shoulder, along);
            if (bendSide.LengthSquared() < _degenerate)
            {
                bendSide = Vector3.Cross(along, Vector3.UnitZ);
                bendSide = bendSide.LengthSquared() < _degenerate ? Vector3.Cross(along, Vector3.UnitX) : bendSide;
            }

            var toElbow = (upperLength * upperLength - foreLength * foreLength + distance * distance) / (2 * distance);
            var height = MathF.Sqrt(MathF.Max(upperLength * upperLength - toElbow * toElbow, 0));
            posed[fore] = shoulder + along * toElbow + Vector3.Normalize(bendSide) * height;
            posed[hand] = shoulder + along * distance;
        }

        return posed;
    }

    // Wrist to the middle knuckle (or the index one): the hand's size, for fitting what it holds.
    private static float PalmLength(Matrix4x4[] world, Dictionary<HumanoidBone, int> roles, string side)
    {
        var hand = world[roles[Enum.Parse<HumanoidBone>(side + "Hand")]].Translation;
        foreach (var knuckle in _knuckles)
        {
            if (roles.TryGetValue(Enum.Parse<HumanoidBone>(side + knuckle), out var index))
            {
                return Vector3.Distance(hand, world[index].Translation);
            }
        }

        return float.NaN;
    }

    private static Dictionary<HumanoidBone, int> RoleIndex(Skeleton skeleton)
    {
        Dictionary<HumanoidBone, int> roles = [];
        foreach (var bone in skeleton.Bones)
        {
            if (bone.Humanoid is { } role)
            {
                roles.TryAdd(role, bone.Index);
            }
        }

        return roles;
    }

    private static IEnumerable<int> Subtree(Skeleton skeleton, int root)
    {
        yield return root;
        foreach (var child in skeleton.Bones.Where(bone => bone.ParentIndex == root && bone.Index != root))
        {
            foreach (var index in Subtree(skeleton, child.Index))
            {
                yield return index;
            }
        }
    }

    private static Matrix4x4[] World(Skeleton skeleton, Func<int, Transform> local)
    {
        var world = new Matrix4x4[skeleton.Bones.Count];
        for (var index = 0; index < skeleton.Bones.Count; index++)
        {
            var parent = skeleton.Bones[index].ParentIndex;
            world[index] = parent >= 0 && parent < index ? Local(local(index)) * world[parent] : Local(local(index));
        }

        return world;
    }

    private static Transform Sample(BoneTransformTrack track, float time)
    {
        var keys = track.Keyframes;
        if (keys.Count == 0)
        {
            return Transform.Identity;
        }

        if (time <= keys[0].TimeSeconds)
        {
            return keys[0].Transform;
        }

        for (var index = 1; index < keys.Count; index++)
        {
            if (time <= keys[index].TimeSeconds)
            {
                var (a, b) = (keys[index - 1], keys[index]);
                var span = b.TimeSeconds - a.TimeSeconds;
                var t = span > 0 ? (time - a.TimeSeconds) / span : 0;
                return new Transform(
                    Vector3.Lerp(a.Transform.Translation, b.Transform.Translation, t),
                    Quaternion.Slerp(a.Transform.Rotation, b.Transform.Rotation, t),
                    Vector3.Lerp(a.Transform.Scale, b.Transform.Scale, t));
            }
        }

        return keys[^1].Transform;
    }

    private static Matrix4x4 Local(Transform transform) =>
        Matrix4x4.CreateScale(transform.Scale) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(transform.Rotation)) * Matrix4x4.CreateTranslation(transform.Translation);

    private static Matrix4x4 Scaled(Matrix4x4 world, float scale) => world with { M41 = world.M41 * scale, M42 = world.M42 * scale, M43 = world.M43 * scale };

    private static Matrix4x4 Rotation(Matrix4x4 world) => world with { M41 = 0, M42 = 0, M43 = 0 };

    private static Matrix4x4 Inverse(Matrix4x4 matrix) => Matrix4x4.Invert(matrix, out var inverse) ? inverse : Matrix4x4.Identity;

    private static Transform ToTransform(Matrix4x4 matrix)
    {
        return Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation)
            ? new Transform(translation, Quaternion.Normalize(rotation), scale)
            : new Transform(matrix.Translation, Quaternion.Identity, Vector3.One);
    }
}
