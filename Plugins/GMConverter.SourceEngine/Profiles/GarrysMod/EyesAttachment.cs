using System.Numerics;
using GMConverter.SDK.Geometry;
using GMConverter.SourceEngine.Common;

namespace GMConverter.SourceEngine.Profiles.GarrysMod;

// Source NPCs see, look and aim from their "eyes" attachment, and the MDL's $eyeposition (written
// from it) is where game code places their view. Models from other engines rarely have one, so it
// is added on the head bone, at the bone's origin, facing the way the model faces.
internal static class EyesAttachment
{
    public const string Name = "eyes";

    private static readonly string[] _ignoredWords = ["target", "ik", "fx", "look", "aim"];

    public static Model AddIfMissing(Model model)
    {
        if (model.Attachments?.Any(attachment => string.Equals(attachment.Name, Name, StringComparison.OrdinalIgnoreCase)) is true ||
            model.Skeleton is not { } skeleton ||
            skeleton.Bones.FirstOrDefault(bone => IsHead(bone.Name)) is not { } head)
        {
            return model;
        }

        // World = local * head world, so the local transform that puts the eyes at the head's origin
        // facing the model's front is that facing (at the head's position) times the head's inverse.
        var headWorld = BoneWorld(skeleton, head.Index);
        var forward = model.Forward is { } facing && new Vector2(facing.X, facing.Y).LengthSquared() > 0.0001f
            ? facing
            : Vector3.UnitX;
        var eyesWorld = Matrix4x4.CreateRotationZ(MathF.Atan2(forward.Y, forward.X)) * Matrix4x4.CreateTranslation(headWorld.Translation);
        Matrix4x4.Invert(headWorld, out var inverseHead);
        Matrix4x4.Decompose(eyesWorld * inverseHead, out _, out var rotation, out var translation);

        var eyes = new Attachment(Name, head.Index, new Transform(translation, Quaternion.Normalize(rotation), Vector3.One));
        return model with { Attachments = [.. model.Attachments ?? [], eyes] };
    }

    private static bool IsHead(string boneName)
    {
        var words = NameWords.Split(boneName);
        return words.Any(word => word is "head" or "head1") && !words.Any(word => _ignoredWords.Contains(word));
    }

    private static Matrix4x4 BoneWorld(Skeleton skeleton, int boneIndex)
    {
        var world = Matrix4x4.Identity;
        for (var bone = skeleton.Bones.FirstOrDefault(candidate => candidate.Index == boneIndex);
             bone is not null;
             bone = bone.ParentIndex >= 0 ? skeleton.Bones.FirstOrDefault(candidate => candidate.Index == bone.ParentIndex) : null)
        {
            var local = bone.LocalBindPose;
            world *= Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(local.Rotation)) * Matrix4x4.CreateTranslation(local.Translation);
        }

        return world;
    }
}
