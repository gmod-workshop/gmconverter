using System.Numerics;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.PhysicsEngine;
using CUE4Parse.UE4.Objects.UObject;
using GMConverter.UnrealEngine.Formats.PSK.Physics;

namespace GMConverter.UnrealEngine.Archives;

/// <summary>
/// Reads a skeletal mesh's physics asset into the PSK physics sidecar. Positions and rotations are
/// mirrored on Y the way CUE4Parse writes the PSK's own points and bones.
/// </summary>
internal static class UnrealPhysicsAssetReader
{
    // Unreal's FConeConstraint/FTwistConstraint defaults. Cooked assets leave out values that
    // match them, which CUE4Parse's typed structs would read as 0 (a locked axis).
    private const float _defaultLimitDegrees = 45f;

    public static PSKPhysicsSidecar? Read(USkeletalMesh skeletalMesh)
    {
        if (!skeletalMesh.PhysicsAsset.TryLoad<UPhysicsAsset>(out var physicsAsset))
        {
            return null;
        }

        List<PSKPhysicsBody> bodies = [];
        foreach (var bodyIndex in physicsAsset.SkeletalBodySetups)
        {
            if (bodyIndex.TryLoad<USkeletalBodySetup>(out var body) &&
                !body.BoneName.IsNone &&
                body.AggGeom is { } geometry &&
                ReadShapes(geometry) is { Count: > 0 } shapes)
            {
                bodies.Add(new PSKPhysicsBody(body.BoneName.Text, shapes));
            }
        }

        List<PSKPhysicsJoint> joints = [];
        foreach (var constraintIndex in physicsAsset.ConstraintSetup)
        {
            if (constraintIndex.TryLoad<UPhysicsConstraintTemplate>(out var constraint) &&
                ReadJoint(constraint) is { } joint)
            {
                joints.Add(joint);
            }
        }

        return bodies.Count == 0 ? null : new PSKPhysicsSidecar(bodies, joints);
    }

    private static List<PSKPhysicsShape> ReadShapes(FKAggregateGeom geometry)
    {
        List<PSKPhysicsShape> shapes = [];
        foreach (var sphere in geometry.SphereElems)
        {
            shapes.Add(new PSKPhysicsShape(PSKPhysicsShapeKind.Sphere, Mirror(sphere.Center), Quaternion.Identity, Radius: sphere.Radius));
        }

        // Unreal stores a box's full size.
        foreach (var box in geometry.BoxElems)
        {
            shapes.Add(new PSKPhysicsShape(
                PSKPhysicsShapeKind.Box,
                Mirror(box.Center),
                Mirror(box.Rotation.Quaternion()),
                HalfExtents: new Vector3(box.X, box.Y, box.Z) * 0.5f));
        }

        foreach (var capsule in geometry.SphylElems)
        {
            shapes.Add(new PSKPhysicsShape(
                PSKPhysicsShapeKind.Capsule,
                Mirror(capsule.Center),
                Mirror(capsule.Rotation.Quaternion()),
                Radius: capsule.Radius,
                Length: capsule.Length));
        }

        // A tapered capsule becomes a straight one with its larger radius; Source collision is
        // a convex hull either way, and the extra bulk errs on the side of not clipping.
        foreach (var capsule in geometry.TaperedCapsuleElems)
        {
            shapes.Add(new PSKPhysicsShape(
                PSKPhysicsShapeKind.Capsule,
                Mirror(capsule.Center),
                Mirror(capsule.Rotation.Quaternion()),
                Radius: MathF.Max(capsule.Radius0, capsule.Radius1),
                Length: capsule.Length));
        }

        foreach (var convex in geometry.ConvexElems)
        {
            if (convex.VertexData.Length < 4)
            {
                continue;
            }

            // A missing transform reads as all zeros, which would collapse the hull to a point.
            var transform = convex.Transform.Scale3D.IsZero() ? FTransform.Identity : convex.Transform;
            shapes.Add(new PSKPhysicsShape(
                PSKPhysicsShapeKind.Convex,
                Vector3.Zero,
                Quaternion.Identity,
                Points: [.. convex.VertexData.Select(vertex => Mirror(transform.TransformPosition(vertex)))]));
        }

        return shapes;
    }

    private static PSKPhysicsJoint? ReadJoint(UPhysicsConstraintTemplate constraint)
    {
        var instance = constraint.GetOrDefault<FStructFallback>("DefaultInstance");
        if (instance is null)
        {
            return null;
        }

        var bone = instance.GetOrDefault<FName>("ConstraintBone1");
        var parentBone = instance.GetOrDefault<FName>("ConstraintBone2");
        if (bone.IsNone || parentBone.IsNone)
        {
            return null;
        }

        var profile = instance.GetOrDefault<FStructFallback>("ProfileInstance");
        var cone = profile?.GetOrDefault<FStructFallback>("ConeLimit");
        var twist = profile?.GetOrDefault<FStructFallback>("TwistLimit");
        return new PSKPhysicsJoint(
            bone.Text,
            parentBone.Text,
            ReadFrame(instance),
            ReadLimit(twist, "TwistMotion", "TwistLimitDegrees"),
            ReadLimit(cone, "Swing1Motion", "Swing1LimitDegrees"),
            ReadLimit(cone, "Swing2Motion", "Swing2LimitDegrees"));
    }

    // The child's constraint frame: its primary axis is X and its secondary axis is Y. Mirroring
    // on Y maps the rotation R to M*R*M (the mirror applied to quaternions above), whose axes
    // are M*X, -M*Y and M*Z, so an untouched frame stays the identity.
    private static Quaternion ReadFrame(FStructFallback instance)
    {
        var primary = Vector3.Normalize(Mirror(instance.GetOrDefault("PriAxis1", new FVector(1f, 0f, 0f))));
        var secondary = -Mirror(instance.GetOrDefault("SecAxis1", new FVector(0f, 1f, 0f)));
        secondary = Vector3.Normalize(secondary - (Vector3.Dot(secondary, primary) * primary));
        if (!float.IsFinite(primary.X) || !float.IsFinite(secondary.X))
        {
            return Quaternion.Identity;
        }

        var third = Vector3.Cross(primary, secondary);
        var basis = new Matrix4x4(
            primary.X, primary.Y, primary.Z, 0f,
            secondary.X, secondary.Y, secondary.Z, 0f,
            third.X, third.Y, third.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
    }

    private static float? ReadLimit(FStructFallback? limit, string motionName, string degreesName)
    {
        var motion = limit?.GetOrDefault(motionName, EAngularConstraintMotion.ACM_Free) ?? EAngularConstraintMotion.ACM_Free;
        return motion switch
        {
            EAngularConstraintMotion.ACM_Locked => 0f,
            EAngularConstraintMotion.ACM_Limited => limit!.GetOrDefault(degreesName, _defaultLimitDegrees),
            _ => null,
        };
    }

    private static Vector3 Mirror(FVector vector) => new(vector.X, -vector.Y, vector.Z);

    private static Quaternion Mirror(FQuat quaternion) => new(-quaternion.X, quaternion.Y, -quaternion.Z, quaternion.W);
}
