using System.Numerics;

namespace GMConverter.Unity.Animation;

/// <summary>
/// Turns a humanoid pose (the body's position and rotation plus 95 muscle values) into local
/// transforms for an avatar's human skeleton. Each bone's muscles rotate it in its muscle frame;
/// the hips are placed so the body's centre of mass and orientation match the pose's. The body
/// orientation is Unity's: right across the upper legs and arms, up from the legs to the arms,
/// which reproduces each avatar's stored reference orientation.
/// </summary>
internal sealed class UnityHumanoidSolver
{
    /// <summary>Muscles in a humanoid pose: 55 for the body, then 20 for each hand.</summary>
    public const int MuscleCount = 95;

    private const int _hips = 0;
    private const int _leftUpperLeg = 1;
    private const int _rightUpperLeg = 2;
    private const int _leftUpperArm = 14;
    private const int _rightUpperArm = 15;
    private const int _bodyMuscleCount = 55;
    private const int _fingerMusclesPerHand = 20;

    // The muscles turning each body bone about its x (twist), y and z axes, or -1.
    private static readonly int[,] _bodyMuscles =
    {
        { -1, -1, -1 }, // Hips
        { 23, 22, 21 }, { 31, 30, 29 }, // Upper legs
        { 25, -1, 24 }, { 33, -1, 32 }, // Lower legs
        { -1, 27, 26 }, { -1, 35, 34 }, // Feet
        { 2, 1, 0 }, { 5, 4, 3 }, { 8, 7, 6 }, // Spine, chest, upper chest
        { 11, 10, 9 }, { 14, 13, 12 }, // Neck, head
        { -1, 38, 37 }, { -1, 47, 46 }, // Shoulders
        { 41, 40, 39 }, { 50, 49, 48 }, // Upper arms
        { 43, -1, 42 }, { 52, -1, 51 }, // Lower arms
        { -1, 45, 44 }, { -1, 54, 53 }, // Hands
        { -1, -1, 28 }, { -1, -1, 36 }, // Toes
        { -1, 16, 15 }, { -1, 18, 17 }, // Eyes
        { -1, 20, 19 }, // Jaw
    };

    private readonly UnityAvatar _avatar;
    private readonly (int X, int Y, int Z)[] _nodeMuscles;
    private readonly UnityXform[] _referenceGlobals;
    private readonly Vector3 _massOffset;

    public UnityHumanoidSolver(UnityAvatar avatar)
    {
        _avatar = avatar;
        _nodeMuscles = new (int, int, int)[avatar.NodeCount];
        Array.Fill(_nodeMuscles, (-1, -1, -1));
        for (var bone = 0; bone < UnityAvatar.BodyBoneCount; bone++)
        {
            if (avatar.BodyNodes[bone] is var node and >= 0)
            {
                _nodeMuscles[node] = (_bodyMuscles[bone, 0], _bodyMuscles[bone, 1], _bodyMuscles[bone, 2]);
            }
        }

        AddFingers(avatar.LeftHandNodes, _bodyMuscleCount);
        AddFingers(avatar.RightHandNodes, _bodyMuscleCount + _fingerMusclesPerHand);

        HipsNode = avatar.BodyNodes[_hips];
        _referenceGlobals = Globals([.. avatar.Pose]);

        // The centre of mass computed from the bones misses the avatar's own by a little; keep the
        // difference, in the body's frame, so the reference pose lands exactly.
        _massOffset = Vector3.Transform(avatar.Root.Translation - MassCenter(_referenceGlobals), Quaternion.Conjugate(avatar.Root.Rotation));
    }

    /// <summary>The node of the hips, which the solved pose moves; -1 when the avatar has none.</summary>
    public int HipsNode { get; }

    /// <summary>
    /// Local transforms for every human skeleton node. <paramref name="muscles"/> holds the 95
    /// muscle values; <paramref name="bodyPosition"/> is the centre of mass in human units and
    /// <paramref name="bodyRotation"/> the body's orientation.
    /// </summary>
    public UnityXform[] Solve(ReadOnlySpan<float> muscles, Vector3 bodyPosition, Quaternion bodyRotation)
    {
        var locals = _avatar.Pose.ToArray();
        for (var node = 0; node < locals.Length; node++)
        {
            var (x, y, z) = _nodeMuscles[node];
            if ((x >= 0 || y >= 0 || z >= 0) && _avatar.Axes[node] is { } axes)
            {
                var values = new Vector3(Muscle(muscles, x), Muscle(muscles, y), Muscle(muscles, z));
                locals[node] = locals[node] with { Rotation = axes.Rotate(values) };
            }
        }

        if (HipsNode < 0)
        {
            return locals;
        }

        // Unity's body orientation comes from the limbs: right across the upper legs and upper
        // arms, up from between the legs to between the arms. Clips store it relative to the
        // avatar's own reference orientation, so they play on any avatar. Find it with the hips
        // unturned; the hips' rotation is whatever turns that onto the pose's.
        var parentGlobal = _avatar.Parents[HipsNode] >= 0 ? _referenceGlobals[_avatar.Parents[HipsNode]] : UnityXform.Identity;
        locals[HipsNode] = Relative(new UnityXform(Vector3.Zero, Quaternion.Identity, _referenceGlobals[HipsNode].Scale), parentGlobal);
        var unturned = BodyOrientation(Globals(locals));
        var hipsRotation = Quaternion.Normalize(bodyRotation * _avatar.Root.Rotation * Quaternion.Conjugate(unturned));

        // Pose the skeleton with the hips at the origin in that orientation to find where the
        // centre of mass sits relative to them, then move the hips to put it at the body position.
        locals[HipsNode] = Relative(new UnityXform(Vector3.Zero, hipsRotation, _referenceGlobals[HipsNode].Scale), parentGlobal);
        var globals = Globals(locals);
        var massCenter = MassCenter(globals) + Vector3.Transform(_massOffset, bodyRotation);
        var hipsPosition = (bodyPosition * _avatar.Scale) - (massCenter - globals[HipsNode].Translation);

        locals[HipsNode] = Relative(new UnityXform(hipsPosition, hipsRotation, _referenceGlobals[HipsNode].Scale), parentGlobal);
        return locals;
    }

    private Quaternion BodyOrientation(UnityXform[] globals)
    {
        if (!TryPosition(globals, _leftUpperLeg, out var leftLeg) || !TryPosition(globals, _rightUpperLeg, out var rightLeg) ||
            !TryPosition(globals, _leftUpperArm, out var leftArm) || !TryPosition(globals, _rightUpperArm, out var rightArm))
        {
            return Quaternion.Identity;
        }

        var right = (rightLeg - leftLeg) + (rightArm - leftArm);
        var up = Vector3.Normalize(((leftArm + rightArm) - (leftLeg + rightLeg)) * 0.5f);
        var forward = Vector3.Normalize(Vector3.Cross(right, up));
        right = Vector3.Cross(up, forward);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f)));
    }

    private bool TryPosition(UnityXform[] globals, int bone, out Vector3 position)
    {
        var node = _avatar.BodyNodes[bone];
        position = node >= 0 ? globals[node].Translation : default;
        return node >= 0;
    }

    private void AddFingers(IReadOnlyList<int> handNodes, int firstMuscle)
    {
        // Per finger: proximal stretch (z) and spread (y), then intermediate and distal stretch.
        for (var finger = 0; finger < 5; finger++)
        {
            var muscle = firstMuscle + (finger * 4);
            Set(handNodes[finger * 3], (-1, muscle + 1, muscle));
            Set(handNodes[(finger * 3) + 1], (-1, -1, muscle + 2));
            Set(handNodes[(finger * 3) + 2], (-1, -1, muscle + 3));
        }

        void Set(int node, (int, int, int) axes)
        {
            if (node >= 0)
            {
                _nodeMuscles[node] = axes;
            }
        }
    }

    private UnityXform[] Globals(UnityXform[] locals)
    {
        var globals = new UnityXform[locals.Length];
        for (var node = 0; node < locals.Length; node++)
        {
            var parent = _avatar.Parents[node];
            globals[node] = parent >= 0 ? locals[node].Under(globals[parent]) : locals[node];
        }

        return globals;
    }

    private Vector3 MassCenter(UnityXform[] globals)
    {
        var center = Vector3.Zero;
        var total = 0f;
        for (var bone = 0; bone < UnityAvatar.BodyBoneCount && bone < _avatar.BodyMasses.Count; bone++)
        {
            if (_avatar.BodyNodes[bone] is var node and >= 0)
            {
                center += globals[node].Translation * _avatar.BodyMasses[bone];
                total += _avatar.BodyMasses[bone];
            }
        }

        return total > 0f ? center / total : Vector3.Zero;
    }

    private static UnityXform Relative(UnityXform global, UnityXform parent)
    {
        var inverse = Quaternion.Conjugate(parent.Rotation);
        return new UnityXform(
            Vector3.Transform(global.Translation - parent.Translation, inverse) / parent.Scale,
            Quaternion.Normalize(inverse * global.Rotation),
            global.Scale / parent.Scale);
    }

    private static float Muscle(ReadOnlySpan<float> muscles, int index)
    {
        return index >= 0 && index < muscles.Length ? muscles[index] : 0f;
    }
}
